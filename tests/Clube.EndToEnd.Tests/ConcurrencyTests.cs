using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Domain.Vouchers;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.EndToEnd.Tests;

// Proves the LedgerVersion concurrency token without threads: two scopes (two DbContexts over the same database) load the
// same member, and the test decides who saves first. Interleaving by hand is what makes the collision certain; real parallel
// requests would be flaky on in-memory SQLite, which locks the whole database.
public class ConcurrencyTests
{
    private const string PerfumeKit = "perfume-kit";

    private static async Task<MembershipId> SubscribeWithOneAllowanceAsync(ClubeApiFactory factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/memberships", new { cpf = "529.982.247-25" });
        var created = await response.Content.ReadFromJsonAsync<MembershipView>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(created);
        await PixWebhook.CreditAsync(factory, client, created.Id, "2026-10");
        return new MembershipId(created.Id);
    }

    private static T Unwrap<T>(Result<T> result) where T : notnull
    {
        if (!result.TryGetValue(out var value, out var error)) throw new InvalidOperationException($"Setup failed: {error.Code}");
        return value;
    }

    private static PostingContext ContextOf(ClubeApiFactory factory, string correlation) =>
        new(factory.Clock.GetUtcNow(), new CallerContext(Actor.PixProvider, correlation));

    private static async Task<Membership> LoadAsync(IForStoringMemberships store, MembershipId id)
    {
        var membership = await store.FindAsync(id, default);
        Assert.NotNull(membership);
        return membership;
    }

    // Reads the balance the way the use case does, then stages the redemption and its outbox message without saving.
    private static async Task StageRedemptionAsync(ClubeApiFactory factory, IForStoringMemberships store, Membership membership, string key)
    {
        var reward = Unwrap(RewardCatalog.Find(PerfumeKit));
        var balance = await store.BalanceOfAsync(membership.Account, default);
        var idempotencyKey = Unwrap(IdempotencyKey.ForRedemption(membership.Id, key));
        var posting = Unwrap(membership.Redeem(reward, balance, idempotencyKey, ContextOf(factory, $"redeem-{key}")));
        store.Append(posting);
        store.Enqueue(OutboxMessage.For(RewardRedeemed.Of(posting, membership.Id, reward)));
    }

    private static void StageAllowance(ClubeApiFactory factory, IForStoringMemberships store, Membership membership, string period)
    {
        var reference = Unwrap(PaymentReference.Parse($"E{Guid.NewGuid():N}"));
        var payment = new AllowancePayment(Unwrap(BillingPeriod.Parse(period)), Membership.MonthlyAllowance, reference);
        store.Append(Unwrap(membership.CreditMonthlyAllowance(payment, ContextOf(factory, $"credit-{period}"))));
    }

    private static long Scalar(ClubeApiFactory factory, string sql) =>
        long.Parse(factory.QueryScalar(sql) ?? "0", CultureInfo.InvariantCulture);

    private static long MemberBalance(ClubeApiFactory factory) =>
        Scalar(factory, "SELECT SUM(SignedCents) FROM ledger_entries WHERE Account LIKE 'member:%'");

    private static long MemberDebits(ClubeApiFactory factory) =>
        Scalar(factory, "SELECT COUNT(*) FROM ledger_entries WHERE Account LIKE 'member:%' AND SignedCents < 0");

    private static long LedgerImbalance(ClubeApiFactory factory) => Scalar(factory, "SELECT SUM(SignedCents) FROM ledger_entries");

    private static long Version(ClubeApiFactory factory) => Scalar(factory, "SELECT LedgerVersion FROM memberships");

    [Fact]
    public async Task RejectsTheSecondRedemptionThatReadTheSameBalance()
    {
        await using var factory = new ClubeApiFactory();
        var id = await SubscribeWithOneAllowanceAsync(factory);
        long versionBefore = Version(factory);
        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var storeA = scopeA.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        var storeB = scopeB.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        await StageRedemptionAsync(factory, storeA, await LoadAsync(storeA, id), "order-a");
        await StageRedemptionAsync(factory, storeB, await LoadAsync(storeB, id), "order-b");

        bool savedA = await storeA.TrySaveChangesAsync(default);
        bool savedB = await storeB.TrySaveChangesAsync(default);

        Assert.True(savedA);
        Assert.False(savedB);
        Assert.Equal(0, MemberBalance(factory));
        Assert.Equal(1, MemberDebits(factory));
        Assert.Equal(1, Scalar(factory, "SELECT COUNT(*) FROM outbox_messages"));
        Assert.Equal(0, Scalar(factory, "SELECT COUNT(*) FROM ledger_transactions WHERE IdempotencyKey LIKE '%order-b'"));
        Assert.Equal(0, LedgerImbalance(factory));
        Assert.Equal(versionBefore + 1, Version(factory));
    }

    [Fact]
    public async Task LetsTheLoserOfACreditAndRedemptionCollisionRunAgainSuccessfully()
    {
        await using var factory = new ClubeApiFactory();
        var id = await SubscribeWithOneAllowanceAsync(factory);
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2026-11-20T15:00:00Z", CultureInfo.InvariantCulture));
        long versionBefore = Version(factory);
        using var creditScope = factory.Services.CreateScope();
        using var redeemScope = factory.Services.CreateScope();
        var creditStore = creditScope.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        var redeemStore = redeemScope.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        var creditMember = await LoadAsync(creditStore, id);
        var redeemMember = await LoadAsync(redeemStore, id);
        StageAllowance(factory, creditStore, creditMember, "2026-11");
        await StageRedemptionAsync(factory, redeemStore, redeemMember, "order-late");

        bool credited = await creditStore.TrySaveChangesAsync(default);
        bool redeemed = await redeemStore.TrySaveChangesAsync(default);
        var retried = await RedeemAsync(factory, id, "order-late");

        Assert.True(credited);
        Assert.False(redeemed);
        Assert.True(retried.TryGetValue(out var view, out _));
        Assert.Equal(2000, view.BalanceCents);
        Assert.Equal(2000, MemberBalance(factory));
        Assert.Equal(1, MemberDebits(factory));
        Assert.Equal(0, LedgerImbalance(factory));
        Assert.Equal(versionBefore + 2, Version(factory));
    }

    // Both racers use one key, so the unique index also stands in their way; the loser fails either way and nothing doubles.
    [Fact]
    public async Task KeepsASingleDebitWhenBothRacersUseTheSameKey()
    {
        await using var factory = new ClubeApiFactory();
        var id = await SubscribeWithOneAllowanceAsync(factory);
        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var storeA = scopeA.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        var storeB = scopeB.ServiceProvider.GetRequiredService<IForStoringMemberships>();
        await StageRedemptionAsync(factory, storeA, await LoadAsync(storeA, id), "same-order");
        await StageRedemptionAsync(factory, storeB, await LoadAsync(storeB, id), "same-order");

        bool savedA = await storeA.TrySaveChangesAsync(default);
        bool savedB = await storeB.TrySaveChangesAsync(default);

        Assert.True(savedA);
        Assert.False(savedB);
        Assert.Equal(1, MemberDebits(factory));
        Assert.Equal(0, LedgerImbalance(factory));
    }

    [Fact]
    public async Task AnswersConflictToTheLoserAndInsufficientBalanceOnItsRetry()
    {
        await using var factory = new ClubeApiFactory();
        var id = await SubscribeWithOneAllowanceAsync(factory);
        using var scopeWinner = factory.Services.CreateScope();
        using var scopeLoser = factory.Services.CreateScope();
        var winner = ManagerOver(factory, scopeWinner.ServiceProvider.GetRequiredService<IForStoringMemberships>(), id);
        // The loser has read the member and the balance by the time its save starts; the winner finishes inside that gap.
        var loserStore = SaveHook.Before(
            scopeLoser.ServiceProvider.GetRequiredService<IForStoringMemberships>(),
            async () => Assert.True((await winner.RedeemRewardAsync(id, PerfumeKit, "order-a", default)).TryGetValue(out _, out _)));
        var loser = ManagerOver(factory, loserStore, id);

        var conflict = await loser.RedeemRewardAsync(id, PerfumeKit, "order-b", default);
        var retried = await RedeemAsync(factory, id, "order-b");

        Assert.False(conflict.TryGetValue(out _, out var conflictError));
        Assert.Equal(MembershipErrors.ConcurrentUpdate, conflictError);
        Assert.Equal(ErrorKind.Conflict, conflictError.Kind);
        Assert.False(retried.TryGetValue(out _, out var retryError));
        Assert.Equal(MembershipErrors.InsufficientBalance, retryError);
        Assert.Equal(1, MemberDebits(factory));
        Assert.Equal(0, MemberBalance(factory));
    }

    private static MembershipManager ManagerOver(ClubeApiFactory factory, IForStoringMemberships store, MembershipId id) =>
        new(store, new MemberCaller(id), factory.Clock);

    // A fresh scope per attempt, which is exactly what a client retrying the HTTP request gets.
    private static async Task<Result<MembershipView>> RedeemAsync(ClubeApiFactory factory, MembershipId id, string key)
    {
        using var scope = factory.Services.CreateScope();
        var manager = ManagerOver(factory, scope.ServiceProvider.GetRequiredService<IForStoringMemberships>(), id);
        return await manager.RedeemRewardAsync(id, PerfumeKit, key, default);
    }

    private sealed class MemberCaller(MembershipId id) : IForIdentifyingTheCaller
    {
        public CallerContext Current { get; } = new(Actor.Member(id), "concurrency-test");
    }

    // Test-side seam: runs a step right before the store saves, with no hook in production code. A proxy keeps the
    // whole port from being re-implemented by hand just to intercept one call.
    private class SaveHook : DispatchProxy
    {
        private IForStoringMemberships? inner;
        private Func<Task>? beforeSave;

        public static IForStoringMemberships Before(IForStoringMemberships inner, Func<Task> beforeSave)
        {
            var proxy = Create<IForStoringMemberships, SaveHook>();
            if (proxy is not SaveHook hook) throw new InvalidOperationException("Proxy has an unexpected type.");
            hook.inner = inner;
            hook.beforeSave = beforeSave;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || inner is null || beforeSave is null) throw new InvalidOperationException("Proxy not configured.");
            if (targetMethod.Name == nameof(IForStoringMemberships.TrySaveChangesAsync) && args is [CancellationToken ct])
                return SaveAfterAsync(beforeSave, inner, ct);

            return targetMethod.Invoke(inner, args);
        }

        private static async Task<bool> SaveAfterAsync(Func<Task> beforeSave, IForStoringMemberships store, CancellationToken ct)
        {
            await beforeSave();
            return await store.TrySaveChangesAsync(ct);
        }
    }
}
