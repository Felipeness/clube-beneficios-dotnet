using Clube.Core.Domain.Reconciliation;

namespace Clube.Core.Ports.Driven;

/// <summary>
/// Driven port: what the payment provider says it settled. The day is a Sao Paulo calendar day. A real adapter pages through
/// the provider's statement itself and enforces its own timeout; a failure is an exception, never an empty list that would
/// read as "nothing settled".
/// </summary>
public interface IForReadingPspSettlements
{
    Task<IReadOnlyList<PspSettlement>> SettlementsOnAsync(DateOnly day, CancellationToken ct);
}
