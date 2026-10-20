using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.Time.Testing;

namespace Clube.Core.Tests;

public class CreditExpiryTests
{
    // Mid-month in Sao Paulo, so a period's credits are posted well inside it.
    private static DateTimeOffset MidMonth(string period) => DateTimeOffset.Parse($"{period}-15T15:00:00Z");

    private readonly InMemoryMembershipStore store = new();
    private readonly FakeTimeProvider clock = new(MidMonth("2025-09"));

    private IForManagingMemberships NewHexagon() =>
        new MembershipManager(store, new FakeCaller(Actor.Operator("operator-1"), "corr-1"), clock);

    private Task<CreditExpiryReport> RunExpiryAsync() =>
        new CreditExpiryRunner(store, clock).RunAsync(CreditExpiry.CutoffAt(clock.GetUtcNow()), default);

    private async Task<MembershipId> SubscribeAndCreditAsync(IForManagingMemberships memberships, string cpf = "529.982.247-25")
    {
        var id = new MembershipId(ResultAssert.Ok(await memberships.SubscribeAsync(cpf, default)).Id);
        ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance(BillingPeriod.At(clock.GetUtcNow()).ToString()), default));
        return id;
    }

    private async Task<long> BalanceAsync(IForManagingMemberships memberships, MembershipId id) =>
        ResultAssert.Ok(await memberships.GetAsync(id, default)).BalanceCents;

    private async Task<Money> BreakageAsync() => await store.BalanceOfAsync(LedgerAccount.Breakage, default);

    private static Guid TransactionId(StatementView statement, string description) =>
        statement.Lines.Single(line => line.Description == description).TransactionId;

    [Theory]
    [InlineData(2000, 0, 2000)]     // old credit never spent: all of it
    [InlineData(2000, 1500, 500)]   // partly spent: only what the debits did not reach
    [InlineData(2000, 2000, 0)]     // spent in full
    [InlineData(0, 0, 0)]           // only recent credit: nothing is old enough
    [InlineData(2000, 3000, 0)]     // negative balance (a Pix refund after spending): nothing, never a negative expiry
    [InlineData(4000, 2000, 2000)]  // two old lots, one already expired: the earlier expiry is a debit, so only the other goes
    public void ExpiresWhatTheDebitsDidNotConsumeOfTheOldCredits(long creditedThroughCutoff, long debitedEver, long expected) =>
        Assert.Equal(Money.FromCents(expected), CreditExpiry.Expiring(new ExpiryTotals(Money.FromCents(creditedThroughCutoff), Money.FromCents(debitedEver))));

    [Theory]
    [InlineData("2026-11-01T03:00:00Z", "2025-10")] // midnight of Nov 1 in Sao Paulo
    [InlineData("2026-11-01T02:59:00Z", "2025-09")] // still Oct 31 in Sao Paulo, already November in UTC
    [InlineData("2026-10-20T15:00:00Z", "2025-09")]
    public void CutsOffAtTheNewestPeriodWhoseCreditsAreAllTwelveMonthsOld(string now, string expected) =>
        Assert.Equal(expected, CreditExpiry.CutoffAt(DateTimeOffset.Parse(now)).ToString());

    [Fact]
    public void EndsAPeriodAtMidnightInSaoPaulo() =>
        Assert.Equal(DateTimeOffset.Parse("2025-11-01T03:00:00Z"), ResultAssert.Ok(BillingPeriod.Parse("2025-10")).EndsAt);

    // Credit posted mid-month of creditedPeriod, optional redemption right after, expiry run on 2026-10-20 (cutoff 2025-09).
    [Theory]
    [InlineData("2025-09", null, 2000, 0)]              // 13 months old and unspent: expires whole
    [InlineData("2025-09", "cinema-ticket", 1000, 0)]   // half spent: only the rest of the old lot
    [InlineData("2025-10", null, 0, 2000)]              // 12 months and some days: waits for the next month
    [InlineData("2026-10", null, 0, 2000)]              // new credit
    public async Task ExpiresOnlyTheUnspentPartOfCreditsPastTheirLifetime(string creditedPeriod, string? redeemed, long expectedExpired, long expectedBalance)
    {
        clock.SetUtcNow(MidMonth(creditedPeriod));
        var memberships = NewHexagon();
        var id = await SubscribeAndCreditAsync(memberships);
        if (redeemed is not null) ResultAssert.Ok(await memberships.RedeemRewardAsync(id, redeemed, "order-1", default));

        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));
        var report = await RunExpiryAsync();

        Assert.Equal(expectedExpired, report.ExpiredCents);
        Assert.Equal(expectedBalance, await BalanceAsync(memberships, id));
        Assert.Equal(Money.FromCents(expectedExpired), await BreakageAsync());
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task PostsTheExpiryAsAnAuditedTransferToBreakageUnderAMonthlyKey()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAndCreditAsync(memberships);
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

        await RunExpiryAsync();

        var expiry = store.PostedTransactions.Last();
        Assert.Equal($"expiry:{id}:2025-09", expiry.Header.Key.Value);
        Assert.Equal("Credit expired (credits up to 2025-09)", expiry.Header.Description);
        Assert.Equal(Actor.CreditExpiry, expiry.Header.Caller.Actor);
        Assert.Equal(ActorKind.System, expiry.Header.Caller.Actor.Kind);
        Assert.StartsWith("credit-expiry:", expiry.Header.Caller.CorrelationId, StringComparison.Ordinal);
        Assert.Equal([LedgerAccount.Member(id), LedgerAccount.Breakage], expiry.Entries.Select(entry => entry.Account));
        // Bumped by the allowance and again by the expiry: the job goes through the same concurrency guard as any posting.
        Assert.Equal(2, (await store.FindAsync(id, default))?.LedgerVersion);
    }

    [Fact]
    public async Task ExpiresNothingWhenTheBalanceIsNegative()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAndCreditAsync(memberships);
        ResultAssert.Ok(await memberships.RedeemRewardAsync(id, "perfume-kit", "order-1", default));
        var statement = ResultAssert.Ok(await memberships.GetStatementAsync(id, StatementPage.Default, default));
        ResultAssert.Ok(await memberships.ReverseTransactionAsync(id, TransactionId(statement, "Monthly allowance 2025-09"), default));
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

        var report = await RunExpiryAsync();

        Assert.Equal(0, report.Expired);
        Assert.Equal(-2000, await BalanceAsync(memberships, id));
    }

    [Fact]
    public async Task ExpiresOnceWhenRunTwiceInTheSameMonth()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAndCreditAsync(memberships);
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

        var first = await RunExpiryAsync();
        clock.Advance(TimeSpan.FromDays(1));
        var second = await RunExpiryAsync();

        Assert.Equal(1, first.Expired);
        Assert.Equal(0, second.Expired);
        Assert.Equal(0, await BalanceAsync(memberships, id));
        Assert.Equal(Money.FromCents(2000), await BreakageAsync());
    }

    [Fact]
    public async Task CountsAnEarlierExpiryAsADebitSoEachLotExpiresOnce()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAndCreditAsync(memberships);
        clock.SetUtcNow(MidMonth("2025-10"));
        ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2025-10"), default));

        clock.SetUtcNow(MidMonth("2026-10"));
        var september = await RunExpiryAsync();
        clock.SetUtcNow(MidMonth("2026-11"));
        var october = await RunExpiryAsync();
        clock.SetUtcNow(MidMonth("2026-12"));
        var november = await RunExpiryAsync();

        Assert.Equal([2000L, 2000L, 0L], new[] { september, october, november }.Select(report => report.ExpiredCents));
        Assert.Equal(0, await BalanceAsync(memberships, id));
        Assert.Equal(Money.FromCents(4000), await BreakageAsync());
    }

    [Fact]
    public async Task GivesTheBalanceBackWhenAnOperatorReversesTheExpiry()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAndCreditAsync(memberships);
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));
        await RunExpiryAsync();
        var statement = ResultAssert.Ok(await memberships.GetStatementAsync(id, StatementPage.Default, default));

        var view = ResultAssert.Ok(await memberships.ReverseTransactionAsync(id, TransactionId(statement, "Credit expired (credits up to 2025-09)"), default));
        clock.SetUtcNow(MidMonth("2026-11"));
        var nextMonth = await RunExpiryAsync();

        Assert.Equal(2000, view.BalanceCents);
        Assert.Equal(Money.Zero, await BreakageAsync());
        Assert.Equal(0, nextMonth.Expired);
    }

    [Fact]
    public async Task CarriesOnPastAMemberThatFailsAndReportsIt()
    {
        var memberships = NewHexagon();
        var broken = await SubscribeAndCreditAsync(memberships);
        var healthy = await SubscribeAndCreditAsync(memberships, "111.444.777-35");
        store.BrokenAccount = LedgerAccount.Member(broken);
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

        var report = await RunExpiryAsync();

        var failure = Assert.Single(report.Failures);
        Assert.Equal(broken, failure.Member);
        Assert.Contains("disk on fire", failure.Error, StringComparison.Ordinal);
        Assert.Equal(0, await BalanceAsync(memberships, healthy));
        Assert.Equal(2000, await BalanceAsync(memberships, broken));
    }

    [Fact]
    public async Task WalksEveryPageOfMembers()
    {
        var memberships = NewHexagon();
        int members = CreditExpiryRunner.PageSize + 1;
        for (int index = 0; index < members; index++) await SubscribeAndCreditAsync(memberships, CpfNumber(index));
        clock.SetUtcNow(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

        var report = await RunExpiryAsync();

        Assert.Equal(members, report.Expired);
        Assert.Equal(Money.FromCents(members * 2000L), await BreakageAsync());
    }

    // A valid CPF per index: nine base digits plus the two check digits.
    internal static string CpfNumber(int index)
    {
        int[] digits = [.. $"{100000000 + index}".Select(digit => digit - '0')];
        int[] withFirst = [.. digits, CheckDigit(digits)];
        return string.Concat(withFirst.Append(CheckDigit(withFirst)));
    }

    private static int CheckDigit(int[] digits)
    {
        int remainder = digits.Select((digit, position) => digit * (digits.Length + 1 - position)).Sum() % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
