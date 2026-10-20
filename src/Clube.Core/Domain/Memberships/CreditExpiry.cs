using Clube.Core.Domain.Ledger;

namespace Clube.Core.Domain.Memberships;

/// <summary>What a member's account holds, summed by the store, for deciding what expires at a cutoff.</summary>
/// <param name="CreditedThroughCutoff">Every credit to the account posted up to the end of the cutoff period (allowances, reversed redemptions).</param>
/// <param name="DebitedEver">Every debit ever posted to the account (redemptions, reversed credits, earlier expiries).</param>
public sealed record ExpiryTotals(Money CreditedThroughCutoff, Money DebitedEver);

// Breakage policy: each credit expires 12 months after it was posted, and debits spend the oldest credit first (FIFO).
public static class CreditExpiry
{
    public const int LifetimeMonths = 12;

    // Month-granular so one expiry per member per month is enough: the cutoff is the newest period whose credits have all
    // lived at least LifetimeMonths. Running anywhere in 2026-11 expires credits posted up to 2025-10.
    public static BillingPeriod CutoffAt(DateTimeOffset now) => BillingPeriod.At(now).AddMonths(-(LifetimeMonths + 1));

    // FIFO without tracking lots: debits consume credits oldest first, so the credits posted up to the cutoff are the first
    // ones consumed, and whatever all debits together did not reach of them is still unspent. Earlier expiries are debits
    // too, so a lot never expires twice; a negative balance (credit reversed after being spent) leaves nothing to expire.
    public static Money Expiring(ExpiryTotals totals) =>
        Money.FromCents(Math.Max(0, totals.CreditedThroughCutoff.Cents - totals.DebitedEver.Cents));
}
