using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Reconciliation;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Application;

public sealed class ReconciliationRunner(IForStoringMemberships ledger, IForReadingPspSettlements psp) : IForReconcilingWithThePsp
{
    public const int PageSize = 500;

    public async Task<ReconciliationReport> RunAsync(DateOnly day, CancellationToken ct)
    {
        var settled = await psp.SettlementsOnAsync(day, ct);
        var credited = await CreditedOnAsync(PostingWindow.ForDay(day), ct);
        credited.AddRange(await CreditedOnAnotherDayAsync(settled, credited, ct));

        return new ReconciliationReport(day, settled.Count, credited.Count, PixReconciliation.Compare(settled, credited));
    }

    // ponytail: a day of Pix is held in memory, fine into the tens of thousands; stream the comparison by reference order beyond that.
    private async Task<List<ReceivedPix>> CreditedOnAsync(PostingWindow window, CancellationToken ct)
    {
        var credited = new List<ReceivedPix>();
        IReadOnlyList<ReceivedPix> page;
        do
        {
            page = await ledger.ReceivedPixAsync(window, credited.Count > 0 ? credited[^1].Reference : null, PageSize, ct);
            credited.AddRange(page);
        }
        while (page.Count == PageSize);

        return credited;
    }

    // A Pix settled just before midnight can be posted just after it: the credit exists, on the next day. Looking the
    // reference up keeps that from paging someone as "money received, nothing credited".
    private async Task<List<ReceivedPix>> CreditedOnAnotherDayAsync(IReadOnlyList<PspSettlement> settled, List<ReceivedPix> credited, CancellationToken ct)
    {
        var known = credited.Select(pix => pix.Reference).ToHashSet();
        var found = new List<ReceivedPix>();
        foreach (var settlement in settled.Where(settlement => !known.Contains(settlement.EndToEndId)))
        {
            var transaction = await ledger.FindTransactionByReferenceAsync(settlement.EndToEndId, ct);
            if (transaction is not null) found.Add(ReceivedPix.Of(settlement.EndToEndId, transaction));
        }

        return found;
    }
}
