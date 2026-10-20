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

    /// <summary>Newest first, by insertion order. Keyset paging: a new posting never shifts or repeats rows already seen.</summary>
    Task<StatementSlice> StatementOfAsync(LedgerAccount account, StatementPage page, CancellationToken ct);

    /// <summary>The fingerprint stored with the transaction posted under this key, or null when the key is unused.</summary>
    Task<string?> FindPostedFingerprintAsync(IdempotencyKey key, CancellationToken ct);

    /// <summary>A posted transaction with its entries, or null when no transaction has this id.</summary>
    Task<PostedTransaction?> FindTransactionAsync(Guid id, CancellationToken ct);

    void Add(Membership membership);

    /// <summary>Append-only: the store must never update or delete a posted transaction.</summary>
    void Append(LedgerTransaction transaction);

    /// <summary>Returns false when another write won the race (stale version, duplicate holder or idempotency key).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);
}
