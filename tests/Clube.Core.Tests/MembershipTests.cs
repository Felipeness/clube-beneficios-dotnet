using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;

namespace Clube.Core.Tests;

public class MembershipTests
{
    private static readonly BillingPeriod October = ResultAssert.Ok(BillingPeriod.Parse("2026-10"));
    private static readonly Reward CinemaTicket = ResultAssert.Ok(RewardCatalog.Find("cinema-ticket"));
    private static readonly Reward ThermalBottle = ResultAssert.Ok(RewardCatalog.Find("thermal-bottle"));

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T15:00:00Z");

    private static Membership NewMembership() =>
        Membership.Subscribe(ResultAssert.Ok(Cpf.Parse("529.982.247-25")), Now);

    private static IdempotencyKey RedemptionKey(Membership membership) =>
        ResultAssert.Ok(IdempotencyKey.ForRedemption(membership.Id, "client-key-1"));

    [Fact]
    public void AllowanceMovesTwentyReaisFromFundingToTheMember()
    {
        var membership = NewMembership();

        var posting = ResultAssert.Ok(membership.CreditMonthlyAllowance(October, Now));

        Assert.Equal(IdempotencyKey.ForAllowance(membership.Id, October), posting.Header.Key);
        Assert.Contains(posting.Entries, entry => entry.Account == membership.Account && entry.SignedCents == 2000);
        Assert.Contains(posting.Entries, entry => entry.Account == LedgerAccount.AllowanceFunding && entry.SignedCents == -2000);
    }

    [Theory]
    [InlineData("2026-09-10T15:00:00Z", "2026-10-20T15:00:00Z", "2026-10", null)]
    [InlineData("2026-09-10T15:00:00Z", "2026-10-20T15:00:00Z", "2026-09", null)]
    [InlineData("2026-09-10T15:00:00Z", "2026-10-20T15:00:00Z", "2026-11", "membership.future_period")]
    [InlineData("2026-09-10T15:00:00Z", "2026-10-20T15:00:00Z", "2026-08", "membership.period_before_subscription")]
    // 00:30 UTC on Nov 1 is still 21:30 on Oct 31 in Sao Paulo.
    [InlineData("2026-09-10T15:00:00Z", "2026-11-01T00:30:00Z", "2026-11", "membership.future_period")]
    [InlineData("2026-09-10T15:00:00Z", "2026-11-01T00:30:00Z", "2026-10", null)]
    // Subscribed at 02:00 UTC on Oct 1, which is still Sep 30 in Sao Paulo.
    [InlineData("2026-10-01T02:00:00Z", "2026-10-20T15:00:00Z", "2026-09", null)]
    public void CreditsOnlyPeriodsBetweenSubscriptionAndNow(string subscribedAt, string now, string period, string? expectedErrorCode)
    {
        var membership = Membership.Subscribe(ResultAssert.Ok(Cpf.Parse("529.982.247-25")), DateTimeOffset.Parse(subscribedAt));

        var result = membership.CreditMonthlyAllowance(ResultAssert.Ok(BillingPeriod.Parse(period)), DateTimeOffset.Parse(now));

        if (expectedErrorCode is null) ResultAssert.Ok(result);
        else Assert.Equal(expectedErrorCode, ResultAssert.Failed(result).Code);
    }

    [Fact]
    public void ChargesThePromotionalPrice()
    {
        var membership = NewMembership();

        var posting = ResultAssert.Ok(membership.Redeem(ThermalBottle, Money.FromCents(3000), RedemptionKey(membership), Now));

        Assert.Equal(2541, ThermalBottle.Price.Cents);
        Assert.Contains(posting.Entries, entry => entry.Account == membership.Account && entry.SignedCents == -2541);
    }

    [Fact]
    public void RejectsRedemptionAboveBalance()
    {
        var membership = NewMembership();

        var error = ResultAssert.Failed(membership.Redeem(CinemaTicket, Money.FromCents(999), RedemptionKey(membership), Now));

        Assert.Equal(MembershipErrors.InsufficientBalance, error);
        Assert.Equal(0, membership.LedgerVersion);
    }

    [Fact]
    public void BumpsLedgerVersionOnEveryPosting()
    {
        var membership = NewMembership();

        membership.CreditMonthlyAllowance(October, Now);
        membership.Redeem(CinemaTicket, Money.FromCents(2000), RedemptionKey(membership), Now);

        Assert.Equal(2, membership.LedgerVersion);
    }
}
