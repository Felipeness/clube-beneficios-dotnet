using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.Time.Testing;

namespace Clube.Core.Tests;

// The payment provider drives the hexagon here: credits come from confirmed payments and refunds reverse them.
public class PaymentTests
{
    private readonly InMemoryMembershipStore store = new();
    private readonly IForManagingMemberships memberships;

    public PaymentTests() =>
        memberships = new MembershipManager(store, new FakeCaller(Actor.PixProvider, "corr-1"), new FakeTimeProvider(DateTimeOffset.Parse("2026-10-20T15:00:00Z")));

    private async Task<MembershipId> SubscribeAsync() =>
        new(ResultAssert.Ok(await memberships.SubscribeAsync("529.982.247-25", default)).Id);

    private async Task<long> BalanceAsync(MembershipId id) => ResultAssert.Ok(await memberships.GetAsync(id, default)).BalanceCents;

    [Fact]
    public async Task CreditsARedeliveredPaymentOnce()
    {
        var id = await SubscribeAsync();
        var payment = Payments.Allowance("2026-10");

        var first = ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, payment, default));
        var redelivered = ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, payment, default));

        Assert.Equal(AllowanceCreditOutcome.Credited, first);
        Assert.Equal(AllowanceCreditOutcome.AlreadyCredited, redelivered);
        Assert.Equal(2000, await BalanceAsync(id));
    }

    [Fact]
    public async Task ReportsASecondPaymentForAPaidPeriodAsDuplicateWithoutCrediting()
    {
        var id = await SubscribeAsync();
        await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default);

        var second = ResultAssert.Ok(await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10"), default));

        Assert.Equal(AllowanceCreditOutcome.DuplicatePayment, second);
        Assert.Equal(2000, await BalanceAsync(id));
    }

    [Theory]
    [InlineData("", "2026-10", 2000, "ledger.invalid_payment_reference")]
    [InlineData("E 123", "2026-10", 2000, "ledger.invalid_payment_reference")]
    [InlineData("E2026", "2026-13", 2000, "membership.invalid_period")]
    [InlineData("E2026", "2026-10", 1999, "membership.allowance_amount_mismatch")]
    [InlineData("E2026", "2026-11", 2000, "membership.future_period")]
    public async Task RejectsPaymentsThatCannotPayTheAllowance(string reference, string period, long amountCents, string expectedCode)
    {
        var id = await SubscribeAsync();

        Error rejected = ResultAssert.Failed(await memberships.CreditMonthlyAllowanceAsync(id, new ReceivedPayment(reference, period, amountCents), default));

        Assert.Equal(expectedCode, rejected.Code);
        Assert.Equal(0, await BalanceAsync(id));
    }

    [Fact]
    public async Task RejectsAReferenceLongerThanSixtyFourCharacters()
    {
        var id = await SubscribeAsync();

        Error rejected = ResultAssert.Failed(await memberships.CreditMonthlyAllowanceAsync(id, Payments.Allowance("2026-10", new string('E', 65)), default));

        Assert.Equal(LedgerErrors.InvalidPaymentReference, rejected);
    }

    [Fact]
    public async Task StampsTheCreditWithTheProviderAndItsReference()
    {
        var id = await SubscribeAsync();
        var payment = Payments.Allowance("2026-10");

        await memberships.CreditMonthlyAllowanceAsync(id, payment, default);
        var posted = Assert.Single(store.PostedTransactions);

        Assert.Equal(Actor.PixProvider, posted.Header.Caller.Actor);
        Assert.Equal(payment.Reference, posted.Header.ExternalReference?.Value);
    }

    [Fact]
    public async Task RefundReversesTheCreditOnceAndBringsTheBalanceBack()
    {
        var id = await SubscribeAsync();
        var payment = Payments.Allowance("2026-10");
        await memberships.CreditMonthlyAllowanceAsync(id, payment, default);

        var refunded = ResultAssert.Ok(await memberships.RefundPaymentAsync(payment.Reference, default));
        var redelivered = ResultAssert.Ok(await memberships.RefundPaymentAsync(payment.Reference, default));

        Assert.Equal(PaymentRefundOutcome.Refunded, refunded);
        Assert.Equal(PaymentRefundOutcome.AlreadyRefunded, redelivered);
        Assert.Equal(0, await BalanceAsync(id));
        Assert.Equal(2, store.PostedTransactions.Count);
    }

    [Fact]
    public async Task RefundOfASpentCreditLeavesADebt()
    {
        var id = await SubscribeAsync();
        var payment = Payments.Allowance("2026-10");
        await memberships.CreditMonthlyAllowanceAsync(id, payment, default);
        await memberships.RedeemRewardAsync(id, "perfume-kit", "order-1", default);

        await memberships.RefundPaymentAsync(payment.Reference, default);

        Assert.Equal(-2000, await BalanceAsync(id));
    }

    [Fact]
    public async Task ReportsARefundOfAnUnknownPaymentWithoutTouchingTheLedger()
    {
        await SubscribeAsync();

        var outcome = ResultAssert.Ok(await memberships.RefundPaymentAsync(Payments.NewReference(), default));

        Assert.Equal(PaymentRefundOutcome.UnknownPayment, outcome);
        Assert.Empty(store.PostedTransactions);
    }
}
