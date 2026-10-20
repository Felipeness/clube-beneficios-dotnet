using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Tests;

internal static class Payments
{
    // A fresh endToEndId per call, like a real payment; pass one explicitly to model a redelivery.
    public static ReceivedPayment Allowance(string period, string? reference = null, long amountCents = 2000) =>
        new(reference ?? NewReference(), period, amountCents);

    public static string NewReference() => $"E{Guid.NewGuid():N}";

    public static AllowancePayment Parsed(BillingPeriod period, long amountCents = 2000) =>
        new(period, Money.FromCents(amountCents), ResultAssert.Ok(PaymentReference.Parse(NewReference())));
}
