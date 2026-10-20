using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Shared;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Tests;

// The test is a driving adapter: it talks to the hexagon only through the driving port,
// with the in-memory driven adapter plugged in. No HTTP, no SQLite.
public class MembershipManagerTests
{
    private const string Holder = "529.982.247-25";

    private static IForManagingMemberships NewHexagon() =>
        new MembershipManager(new InMemoryMembershipStore(), TimeProvider.System);

    private static async Task<MembershipId> SubscribeAsync(IForManagingMemberships memberships) =>
        new(ResultAssert.Ok(await memberships.SubscribeAsync(Holder, default)).Id);

    [Fact]
    public async Task CreditsEachPeriodOnlyOnce()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);

        await memberships.CreditMonthlyAllowanceAsync(id, "2026-10", default);
        var retried = ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, "2026-10", default));

        Assert.Equal(Membership.MonthlyAllowance.Cents, retried.BalanceCents);
    }

    [Fact]
    public async Task ChargesARetriedRedemptionOnce()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, "2026-10", default);

        await memberships.RedeemRewardAsync(id, "cinema-ticket", "order-42", default);
        var retried = ResultAssert.Ok(await memberships.RedeemRewardAsync(id, "cinema-ticket", "order-42", default));

        Assert.Equal(1000, retried.BalanceCents);
    }

    [Fact]
    public async Task RejectsReusedKeyForADifferentReward()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, "2026-10", default);
        await memberships.RedeemRewardAsync(id, "cinema-ticket", "order-42", default);

        Error reused = ResultAssert.Failed(await memberships.RedeemRewardAsync(id, "thermal-bottle", "order-42", default));
        long balance = ResultAssert.Ok(await memberships.GetAsync(id, default)).BalanceCents;

        Assert.Equal(LedgerErrors.IdempotencyKeyReused, reused);
        Assert.Equal(1000, balance);
    }

    [Fact]
    public async Task ExplainsTheBalanceThroughTheStatement()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, "2026-10", default);
        await memberships.CreditMonthlyAllowanceAsync(id, "2026-11", default);
        await memberships.RedeemRewardAsync(id, "thermal-bottle", "order-1", default);

        var statement = ResultAssert.Ok(await memberships.GetStatementAsync(id, default));

        Assert.Equal(4000 - 2541, statement.BalanceCents);
        Assert.Equal([-2541L, 2000L, 2000L], statement.Lines.Select(line => line.AmountCents));
        Assert.Equal(statement.BalanceCents, statement.Lines.Sum(line => line.AmountCents));
    }

    [Fact]
    public async Task RejectsSecondMembershipForSameHolder()
    {
        var memberships = NewHexagon();
        await memberships.SubscribeAsync(Holder, default);

        var error = ResultAssert.Failed(await memberships.SubscribeAsync(Holder, default));

        Assert.Equal(MembershipErrors.AlreadySubscribed, error);
    }

    [Fact]
    public async Task ReportsUnknownMembership()
    {
        var error = ResultAssert.Failed(await NewHexagon().GetAsync(MembershipId.New(), default));

        Assert.Equal(MembershipErrors.NotFound, error);
    }
}
