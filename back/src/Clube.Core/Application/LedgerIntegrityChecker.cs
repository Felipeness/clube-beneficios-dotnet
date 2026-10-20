using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Application;

public sealed class LedgerIntegrityChecker(IForStoringMemberships memberships) : IForCheckingLedgerIntegrity
{
    public Task<long> ImbalanceCentsAsync(CancellationToken ct) => memberships.LedgerImbalanceAsync(ct);
}
