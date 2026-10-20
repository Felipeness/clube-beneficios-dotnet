using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Reconciliation;

public enum DivergenceKind
{
    // The PSP settled money the ledger never credited: the member paid and got nothing. The worst case.
    MissingInLedger,
    // The ledger credited a Pix the PSP did not settle that day: credit without money.
    MissingAtPsp,
    AmountMismatch,
}

/// <summary>A Pix the PSP settled. <paramref name="Returned"/> means it was sent back in full (refund, MED, duplicate payment).</summary>
public sealed record PspSettlement(PaymentReference EndToEndId, Money Amount, DateTimeOffset SettledAt, bool Returned);

/// <summary>A Pix the ledger credited: what the transaction carrying the provider's reference put into the member's account.</summary>
public sealed record ReceivedPix(PaymentReference Reference, Money Amount)
{
    public static ReceivedPix Of(PaymentReference reference, PostedTransaction transaction) => new(
        reference,
        Money.FromCents(transaction.Entries.Where(entry => entry.Direction == EntryDirection.Credit).Sum(entry => entry.Amount.Cents)));
}

public sealed record Divergence(DivergenceKind Kind, PaymentReference EndToEndId, Money? PspAmount, Money? LedgerAmount);

// Both sides are compared by receipt. A refund is a second, separate event on each side (an outgoing Pix at the PSP, a reversal
// in the ledger), so the original receipt stays matched and the refund is not compared. The one case that needs care is a Pix the
// PSP says was returned that the ledger never credited (a duplicate payment): that is consistent, not a missing credit.
public static class PixReconciliation
{
    public static IReadOnlyList<Divergence> Compare(IReadOnlyList<PspSettlement> settled, IReadOnlyList<ReceivedPix> received)
    {
        var settledByReference = settled.DistinctBy(settlement => settlement.EndToEndId).ToDictionary(settlement => settlement.EndToEndId);
        var receivedByReference = received.ToDictionary(pix => pix.Reference);

        var fromPsp = settledByReference.Values.Select(settlement => Classify(settlement, receivedByReference.GetValueOrDefault(settlement.EndToEndId)));
        var onlyInLedger = received
            .Where(pix => !settledByReference.ContainsKey(pix.Reference))
            .Select(pix => new Divergence(DivergenceKind.MissingAtPsp, pix.Reference, null, pix.Amount));

        return [.. fromPsp.OfType<Divergence>().Concat(onlyInLedger).OrderBy(divergence => divergence.EndToEndId.Value, StringComparer.Ordinal)];
    }

    private static Divergence? Classify(PspSettlement settlement, ReceivedPix? credited)
    {
        if (credited is null)
            return settlement.Returned ? null : new Divergence(DivergenceKind.MissingInLedger, settlement.EndToEndId, settlement.Amount, null);

        return credited.Amount == settlement.Amount
            ? null
            : new Divergence(DivergenceKind.AmountMismatch, settlement.EndToEndId, settlement.Amount, credited.Amount);
    }
}

/// <summary>The instants [From, Until) of one Sao Paulo day, as an absolute range a store can filter postings by.</summary>
public sealed record PostingWindow(DateTimeOffset From, DateTimeOffset Until)
{
    public static PostingWindow ForDay(DateOnly day) => new(ClubeCalendar.StartOf(day), ClubeCalendar.StartOf(day.AddDays(1)));
}
