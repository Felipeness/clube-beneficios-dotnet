using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;

namespace Clube.Core.Tests;

public class LedgerTransactionTests
{
    private static readonly LedgerAccount Member = LedgerAccount.Member(MembershipId.New());
    private static readonly Money TenReais = Money.FromCents(1000);

    private static TransactionHeader Header() =>
        new(IdempotencyKey.ForAllowance(MembershipId.New(), ResultAssert.Ok(BillingPeriod.Parse("2026-10"))), "test", DateTimeOffset.UnixEpoch);

    [Fact]
    public void TransferDebitsOneAccountAndCreditsTheOther()
    {
        var transfer = ResultAssert.Ok(LedgerTransaction.Transfer(LedgerAccount.AllowanceFunding, Member, TenReais, Header()));

        Assert.Equal(0, transfer.Entries.Sum(entry => entry.SignedCents));
        Assert.Contains(transfer.Entries, entry => entry.Account == Member && entry.SignedCents == 1000);
        Assert.Contains(transfer.Entries, entry => entry.Account == LedgerAccount.AllowanceFunding && entry.SignedCents == -1000);
    }

    [Fact]
    public void RejectsUnbalancedEntries()
    {
        LedgerEntry[] entries =
        [
            new(LedgerAccount.AllowanceFunding, TenReais, EntryDirection.Debit),
            new(Member, Money.FromCents(999), EntryDirection.Credit),
        ];

        Assert.Equal(LedgerErrors.Unbalanced, ResultAssert.Failed(LedgerTransaction.Post(Header(), entries)));
    }

    [Fact]
    public void RejectsSingleEntry() =>
        Assert.Equal(
            LedgerErrors.TooFewEntries,
            ResultAssert.Failed(LedgerTransaction.Post(Header(), [new LedgerEntry(Member, TenReais, EntryDirection.Credit)])));

    [Fact]
    public void RejectsZeroAmount() =>
        Assert.Equal(
            LedgerErrors.NonPositiveAmount,
            ResultAssert.Failed(LedgerTransaction.Transfer(LedgerAccount.AllowanceFunding, Member, Money.Zero, Header())));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123x")]
    public void RejectsUnusableClientIdempotencyKey(string clientKey) =>
        Assert.Equal(LedgerErrors.InvalidIdempotencyKey, ResultAssert.Failed(IdempotencyKey.ForRedemption(MembershipId.New(), clientKey)));
}
