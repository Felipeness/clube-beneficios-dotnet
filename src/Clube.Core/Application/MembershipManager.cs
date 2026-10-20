using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Application;

// Implements the driving port by orchestrating the domain and calling out through driven ports.
// TimeProvider is a driven port too; the BCL already ships it as an abstraction with a fake for tests.
public sealed class MembershipManager(IForStoringMemberships memberships, IForIdentifyingTheCaller caller, TimeProvider clock) : IForManagingMemberships
{
    private const int StatementPageSize = 50;

    public async Task<Result<MembershipView>> SubscribeAsync(string cpf, CancellationToken ct)
    {
        if (!Cpf.Parse(cpf).TryGetValue(out var holder, out var invalidCpf)) return invalidCpf;

        var membership = Membership.Subscribe(holder, clock.GetUtcNow());
        memberships.Add(membership);

        // The store's uniqueness rule is the source of truth; a pre-check would still race.
        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.AlreadySubscribed;
        return MembershipView.From(membership, Money.Zero);
    }

    public async Task<Result<MembershipView>> GetAsync(MembershipId id, CancellationToken ct)
    {
        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;

        return await ViewOfAsync(membership, ct);
    }

    public async Task<Result<StatementView>> GetStatementAsync(MembershipId id, CancellationToken ct)
    {
        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;

        var balance = await memberships.BalanceOfAsync(membership.Account, ct);
        var lines = await memberships.StatementOfAsync(membership.Account, StatementPageSize, ct);
        return new StatementView(id.Value, balance.Cents, lines);
    }

    public async Task<Result<MembershipView>> CreditMonthlyAllowanceAsync(MembershipId id, string period, CancellationToken ct)
    {
        if (!BillingPeriod.Parse(period).TryGetValue(out var billingPeriod, out var invalidPeriod)) return invalidPeriod;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        // Fast path for retries; the unique index on the key is what actually guarantees it.
        if (await memberships.FindPostedFingerprintAsync(IdempotencyKey.ForAllowance(id, billingPeriod), ct) is not null) return await ViewOfAsync(membership, ct);
        if (!membership.CreditMonthlyAllowance(billingPeriod, PostingContextNow()).TryGetValue(out var posting, out var rejected)) return rejected;

        return await PostAsync(membership, posting, ct);
    }

    public async Task<Result<MembershipView>> RedeemRewardAsync(MembershipId id, string rewardId, string idempotencyKey, CancellationToken ct)
    {
        if (!RewardCatalog.Find(rewardId).TryGetValue(out var reward, out var unknownReward)) return unknownReward;
        if (!IdempotencyKey.ForRedemption(id, idempotencyKey).TryGetValue(out var key, out var invalidKey)) return invalidKey;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        // A retried request answers with the current state and charges nothing; the same key for another reward is refused.
        string? postedFingerprint = await memberships.FindPostedFingerprintAsync(key, ct);
        if (postedFingerprint is not null)
            return postedFingerprint == Membership.RedemptionFingerprint(reward) ? await ViewOfAsync(membership, ct) : LedgerErrors.IdempotencyKeyReused;

        var balance = await memberships.BalanceOfAsync(membership.Account, ct);
        if (!membership.Redeem(reward, balance, key, PostingContextNow()).TryGetValue(out var posting, out var rejected)) return rejected;

        return await PostAsync(membership, posting, ct);
    }

    private PostingContext PostingContextNow() => new(clock.GetUtcNow(), caller.Current);

    private async Task<Result<MembershipView>> PostAsync(Membership membership, LedgerTransaction posting, CancellationToken ct)
    {
        memberships.Append(posting);
        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.ConcurrentUpdate;

        return await ViewOfAsync(membership, ct);
    }

    private async Task<MembershipView> ViewOfAsync(Membership membership, CancellationToken ct) =>
        MembershipView.From(membership, await memberships.BalanceOfAsync(membership.Account, ct));
}
