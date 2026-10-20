using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;

namespace Clube.Core.Tests;

public class MembershipTests
{
    private static readonly BillingPeriod October = ResultAssert.Ok(BillingPeriod.Parse("2026-10"));
    private static readonly Reward CinemaTicket = ResultAssert.Ok(RewardCatalog.Find("cinema-ticket"));
    private static readonly Reward ThermalBottle = ResultAssert.Ok(RewardCatalog.Find("thermal-bottle"));

    private static Membership NewMembership() =>
        Membership.Subscribe(ResultAssert.Ok(Cpf.Parse("529.982.247-25")), DateTimeOffset.UnixEpoch);

    private static IdempotencyKey RedemptionKey(Membership membership) =>
        ResultAssert.Ok(IdempotencyKey.ForRedemption(membership.Id, "client-key-1"));

    [Fact]
    public void AllowanceMovesTwentyReaisFromFundingToTheMember()
    {
        var membership = NewMembership();

        var posting = ResultAssert.Ok(membership.CreditMonthlyAllowance(October, DateTimeOffset.UnixEpoch));

        Assert.Equal(IdempotencyKey.ForAllowance(membership.Id, October), posting.Header.Key);
        Assert.Contains(posting.Entries, entry => entry.Account == membership.Account && entry.SignedCents == 2000);
        Assert.Contains(posting.Entries, entry => entry.Account == LedgerAccount.AllowanceFunding && entry.SignedCents == -2000);
    }

    [Fact]
    public void ChargesThePromotionalPrice()
    {
        var membership = NewMembership();

        var posting = ResultAssert.Ok(membership.Redeem(ThermalBottle, Money.FromCents(3000), RedemptionKey(membership), DateTimeOffset.UnixEpoch));

        Assert.Equal(2541, ThermalBottle.Price.Cents);
        Assert.Contains(posting.Entries, entry => entry.Account == membership.Account && entry.SignedCents == -2541);
    }

    [Fact]
    public void RejectsRedemptionAboveBalance()
    {
        var membership = NewMembership();

        var error = ResultAssert.Failed(membership.Redeem(CinemaTicket, Money.FromCents(999), RedemptionKey(membership), DateTimeOffset.UnixEpoch));

        Assert.Equal(MembershipErrors.InsufficientBalance, error);
        Assert.Equal(0, membership.LedgerVersion);
    }

    [Fact]
    public void BumpsLedgerVersionOnEveryPosting()
    {
        var membership = NewMembership();

        membership.CreditMonthlyAllowance(October, DateTimeOffset.UnixEpoch);
        membership.Redeem(CinemaTicket, Money.FromCents(2000), RedemptionKey(membership), DateTimeOffset.UnixEpoch);

        Assert.Equal(2, membership.LedgerVersion);
    }
}
