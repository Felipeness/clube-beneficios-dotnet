using Clube.Core.Application;
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

    [Fact]
    public async Task CreditsAndRedeemsThroughTheDrivingPort()
    {
        var memberships = NewHexagon();
        var created = ResultAssert.Ok(await memberships.SubscribeAsync(Holder, default));
        var id = new MembershipId(created.Id);

        await memberships.CreditMonthlyAllowanceAsync(id, "2026-10", default);
        var redeemed = ResultAssert.Ok(await memberships.RedeemRewardAsync(id, "cinema-ticket", default));

        Assert.Equal(10, redeemed.Balance);
        Assert.Equal("2026-10", redeemed.LastCreditedPeriod);
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
