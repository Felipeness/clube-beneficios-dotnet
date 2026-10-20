using System.Collections.Immutable;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Reconciliation;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driven;

namespace Clube.Adapters.Partners;

// Stand-in for the PSP's settlement statement until the real contract exists: serves whatever was added to it, filtered by
// Sao Paulo day like the real one must. Empty by default, so in a real deployment every credited Pix shows as missing at
// the PSP (a WARN, never the ERROR reserved for money received and not credited).
public sealed class ConfigurablePspSettlements : IForReadingPspSettlements
{
    private ImmutableList<PspSettlement> settlements = [];

    public void Add(PspSettlement settlement) => ImmutableInterlocked.Update(ref settlements, current => current.Add(settlement));

    // What the PSP does when a payment is sent back: the settlement stays on the statement, flagged as returned.
    public void MarkReturned(PaymentReference reference) => ImmutableInterlocked.Update(
        ref settlements,
        current => current.ConvertAll(settlement => settlement.EndToEndId == reference ? settlement with { Returned = true } : settlement));

    public Task<IReadOnlyList<PspSettlement>> SettlementsOnAsync(DateOnly day, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PspSettlement>>([.. settlements.Where(settlement => ClubeCalendar.DayOf(settlement.SettledAt) == day)]);
}
