using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.Time.Testing;

namespace Clube.Core.Tests;

public class ReversalTests
{
    private static readonly PostingContext Context = new(DateTimeOffset.Parse("2026-10-20T15:00:00Z"), new CallerContext(Actor.Anonymous, "corr-1"));

    private static readonly Actor Staff = Actor.Operator("operator-1");

    private static IForManagingMemberships NewHexagon(InMemoryMembershipStore? store = null, Actor? actor = null) =>
        new MembershipManager(
            store ?? new InMemoryMembershipStore(),
            new FakeCaller(actor ?? Staff, "corr-1"),
            new FakeTimeProvider(Context.At));

    private static async Task<MembershipId> SubscribeAsync(IForManagingMemberships memberships, string cpf = "529.982.247-25") =>
        new(ResultAssert.Ok(await memberships.SubscribeAsync(cpf, default)).Id);

    private static async Task<Guid> TransactionIdAsync(IForManagingMemberships memberships, MembershipId id, string description)
    {
        var statement = ResultAssert.Ok(await memberships.GetStatementAsync(id, StatementPage.Default, default));
        return statement.Lines.Single(line => line.Description == description).TransactionId;
    }

    [Fact]
    public void MirrorsEveryEntryAndKeepsTheSumAtZero()
    {
        var member = LedgerAccount.Member(MembershipId.New());
        var original = new PostedTransaction(Guid.NewGuid(), "Redemption perfume-kit",
            [new LedgerEntry(member, Money.FromCents(2000), EntryDirection.Debit), new LedgerEntry(LedgerAccount.Redemptions, Money.FromCents(2000), EntryDirection.Credit)], null);

        var reversal = ResultAssert.Ok(LedgerTransaction.Reverse(original, Context));

        Assert.Equal([EntryDirection.Credit, EntryDirection.Debit], reversal.Entries.Select(entry => entry.Direction));
        Assert.Equal(0, reversal.Entries.Sum(entry => entry.SignedCents));
        Assert.Equal("Reversal of Redemption perfume-kit", reversal.Header.Description);
        Assert.Equal(original.Id, reversal.Header.ReversesTransactionId);
        Assert.Equal($"reversal:{original.Id}", reversal.Header.Key.Value);
    }

    [Fact]
    public void RejectsReversingAReversal()
    {
        var original = new PostedTransaction(Guid.NewGuid(), "Reversal of x", [], Guid.NewGuid());

        Assert.Equal(LedgerErrors.ReversalOfReversal, ResultAssert.Failed(LedgerTransaction.Reverse(original, Context)));
    }

    [Fact]
    public async Task ReturnsRedeemedValueToTheMember()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default);
        await memberships.RedeemRewardAsync(id, "perfume-kit", "order-1", default);

        var view = ResultAssert.Ok(await memberships.ReverseTransactionAsync(id, await TransactionIdAsync(memberships, id, "Redemption perfume-kit"), default));

        Assert.Equal(2000, view.BalanceCents);
    }

    [Fact]
    public async Task ReversesOnlyOnceWhenAskedTwice()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default);
        Guid credit = await TransactionIdAsync(memberships, id, "Monthly allowance 2026-10");

        await memberships.ReverseTransactionAsync(id, credit, default);
        var again = ResultAssert.Ok(await memberships.ReverseTransactionAsync(id, credit, default));
        var statement = ResultAssert.Ok(await memberships.GetStatementAsync(id, StatementPage.Default, default));

        Assert.Equal(0, again.BalanceCents);
        Assert.Equal(2, statement.Lines.Count);
    }

    [Fact]
    public async Task RefusesToReverseAReversal()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default);
        await memberships.ReverseTransactionAsync(id, await TransactionIdAsync(memberships, id, "Monthly allowance 2026-10"), default);
        Guid reversal = await TransactionIdAsync(memberships, id, "Reversal of Monthly allowance 2026-10");

        Assert.Equal(LedgerErrors.ReversalOfReversal, ResultAssert.Failed(await memberships.ReverseTransactionAsync(id, reversal, default)));
    }

    [Fact]
    public async Task LeavesADebtWhenTheReversedCreditWasAlreadySpentAndBlocksRedemptions()
    {
        var memberships = NewHexagon();
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default);
        await memberships.RedeemRewardAsync(id, "perfume-kit", "order-1", default);

        var view = ResultAssert.Ok(await memberships.ReverseTransactionAsync(id, await TransactionIdAsync(memberships, id, "Monthly allowance 2026-10"), default));
        Error blocked = ResultAssert.Failed(await memberships.RedeemRewardAsync(id, "cinema-ticket", "order-2", default));

        Assert.Equal(-2000, view.BalanceCents);
        Assert.Equal(MembershipErrors.InsufficientBalance, blocked);
    }

    [Fact]
    public async Task ReportsATransactionOfAnotherMemberAsNotFound()
    {
        var memberships = NewHexagon();
        var owner = await SubscribeAsync(memberships);
        var stranger = await SubscribeAsync(memberships, "111.444.777-35");
        await memberships.CreditMonthlyAllowanceAsync(owner, Payments.Allowance("2026-10"), default);
        Guid credit = await TransactionIdAsync(memberships, owner, "Monthly allowance 2026-10");

        Error foreign = ResultAssert.Failed(await memberships.ReverseTransactionAsync(stranger, credit, default));
        Error unknown = ResultAssert.Failed(await memberships.ReverseTransactionAsync(owner, Guid.NewGuid(), default));

        Assert.Equal(MembershipErrors.TransactionNotFound, foreign);
        Assert.Equal(MembershipErrors.TransactionNotFound, unknown);
        Assert.Equal(2000, ResultAssert.Ok(await memberships.GetAsync(owner, default)).BalanceCents);
    }

    [Fact]
    public async Task StampsTheOperatorOnTheReversal()
    {
        var store = new InMemoryMembershipStore();
        var memberships = NewHexagon(store);
        var id = await SubscribeAsync(memberships);
        await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default);

        ResultAssert.Ok(await memberships.ReverseTransactionAsync(id, await TransactionIdAsync(memberships, id, "Monthly allowance 2026-10"), default));

        Assert.Equal(Staff, store.PostedTransactions.Last().Header.Caller.Actor);
    }
}
