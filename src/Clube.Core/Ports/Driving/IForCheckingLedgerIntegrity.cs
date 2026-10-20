namespace Clube.Core.Ports.Driving;

/// <summary>Driving port for the health probe: proves the double-entry invariant over the whole ledger.</summary>
public interface IForCheckingLedgerIntegrity
{
    /// <summary>Sum of every entry, in cents. Zero when the books balance; anything else means a posting bypassed the domain.</summary>
    Task<long> ImbalanceCentsAsync(CancellationToken ct);
}
