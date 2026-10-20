using Clube.Domain.Memberships;

namespace Clube.Domain.Tests;

public class MembershipTests
{
    private static readonly BillingPeriod October = ResultAssert.Ok(BillingPeriod.Parse("2026-10"));
    private static readonly BillingPeriod November = ResultAssert.Ok(BillingPeriod.Parse("2026-11"));
    private static readonly Reward CinemaTicket = ResultAssert.Ok(RewardCatalog.Find("cinema-ticket"));

    private static Membership NewMembership() =>
        Membership.Subscribe(ResultAssert.Ok(Cpf.Parse("529.982.247-25")), DateTimeOffset.UnixEpoch);

    [Fact]
    public void CreditsTheSamePeriodOnlyOnce()
    {
        var membership = NewMembership();

        membership.CreditMonthlyAllowance(October);
        var balance = ResultAssert.Ok(membership.CreditMonthlyAllowance(October));

        Assert.Equal(Membership.MonthlyAllowance, balance);
    }

    [Fact]
    public void AccumulatesSequentialPeriods()
    {
        var membership = NewMembership();

        membership.CreditMonthlyAllowance(October);
        var balance = ResultAssert.Ok(membership.CreditMonthlyAllowance(November));

        Assert.Equal(Credits.Of(40), balance);
        Assert.Equal(November, membership.LastCreditedPeriod);
    }

    [Fact]
    public void RejectsPeriodBeforeLastCredit()
    {
        var membership = NewMembership();
        membership.CreditMonthlyAllowance(November);

        var error = ResultAssert.Failed(membership.CreditMonthlyAllowance(October));

        Assert.Equal(MembershipErrors.PeriodOutOfOrder, error);
        Assert.Equal(Membership.MonthlyAllowance, membership.Balance);
    }

    [Fact]
    public void RedeemsRewardWhenBalanceCoversCost()
    {
        var membership = NewMembership();
        membership.CreditMonthlyAllowance(October);

        var balance = ResultAssert.Ok(membership.Redeem(CinemaTicket));

        Assert.Equal(Credits.Of(10), balance);
    }

    [Fact]
    public void RejectsRedemptionWithoutEnoughCredits()
    {
        var membership = NewMembership();

        var error = ResultAssert.Failed(membership.Redeem(CinemaTicket));

        Assert.Equal(MembershipErrors.InsufficientCredits, error);
        Assert.Equal(Credits.Zero, membership.Balance);
    }
}
