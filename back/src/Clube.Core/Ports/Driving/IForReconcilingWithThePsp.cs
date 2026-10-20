using Clube.Core.Domain.Reconciliation;

namespace Clube.Core.Ports.Driving;

/// <summary>Driving port for whatever triggers the Pix reconciliation (a daily hosted loop, an operator's request, a test).</summary>
public interface IForReconcilingWithThePsp
{
    /// <summary>Compares the Pix the PSP settled on <paramref name="day"/> (Sao Paulo) with the ones the ledger credited that day. Read-only.</summary>
    Task<ReconciliationReport> RunAsync(DateOnly day, CancellationToken ct);
}

public sealed record ReconciliationReport(DateOnly Day, int SettledAtPsp, int CreditedInLedger, IReadOnlyList<Divergence> Divergences)
{
    public int MissingInLedger => Divergences.Count(divergence => divergence.Kind == DivergenceKind.MissingInLedger);
}
