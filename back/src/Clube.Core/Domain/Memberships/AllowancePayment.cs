using Clube.Core.Domain.Ledger;

namespace Clube.Core.Domain.Memberships;

// A confirmed payment for one billing period, as the payment provider reported it.
public sealed record AllowancePayment(BillingPeriod Period, Money Amount, PaymentReference Reference);
