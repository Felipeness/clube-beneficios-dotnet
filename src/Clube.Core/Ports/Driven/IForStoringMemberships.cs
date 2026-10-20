using Clube.Core.Domain.Memberships;

namespace Clube.Core.Ports.Driven;

/// <summary>
/// Driven (secondary) port: what the hexagon needs from a store, described in its own terms.
/// The hexagon owns this contract; SQLite and the in-memory test store are adapters that fulfil it.
/// </summary>
public interface IForStoringMemberships
{
    Task<Membership?> FindAsync(MembershipId id, CancellationToken ct);

    void Add(Membership membership);

    /// <summary>Returns false when another write won the race (stale balance or duplicate holder).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);
}
