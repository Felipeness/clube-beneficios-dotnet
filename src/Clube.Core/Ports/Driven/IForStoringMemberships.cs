using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Vouchers;

namespace Clube.Core.Ports.Driven;

/// <summary>
/// Driven (secondary) port: what the hexagon needs from a store, described in its own terms.
/// Memberships, their ledger and the outbox share one unit of work, so a posting, the version bump
/// that guards it and the message telling the partner about it commit together or not at all.
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

    /// <summary>The transaction posted for this provider payment, or null when no posting carries the reference.</summary>
    Task<PostedTransaction?> FindTransactionByReferenceAsync(PaymentReference reference, CancellationToken ct);

    void Add(Membership membership);

    /// <summary>Append-only: the store must never update or delete a posted transaction.</summary>
    void Append(LedgerTransaction transaction);

    /// <summary>Transactional outbox: commits with the posting it describes in the next save, or not at all.</summary>
    void Enqueue(OutboxMessage message);

    /// <summary>Returns false when another write won the race (stale version, duplicate holder or idempotency key).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);
}
