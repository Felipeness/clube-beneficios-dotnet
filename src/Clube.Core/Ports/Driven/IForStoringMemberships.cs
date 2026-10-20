using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;

namespace Clube.Core.Ports.Driven;

/// <summary>
/// Driven (secondary) port: what the hexagon needs from a store, described in its own terms.
/// Memberships and their ledger share one unit of work, so a posting and the version bump
/// that guards it commit together or not at all.
/// </summary>
public interface IForStoringMemberships
{
    Task<Membership?> FindAsync(MembershipId id, CancellationToken ct);

    Task<Money> BalanceOfAsync(LedgerAccount account, CancellationToken ct);

    Task<IReadOnlyList<StatementLine>> StatementOfAsync(LedgerAccount account, int limit, CancellationToken ct);

    Task<bool> HasPostedAsync(IdempotencyKey key, CancellationToken ct);

    void Add(Membership membership);

    /// <summary>Append-only: the store must never update or delete a posted transaction.</summary>
    void Append(LedgerTransaction transaction);

    /// <summary>Returns false when another write won the race (stale version, duplicate holder or idempotency key).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);
}
