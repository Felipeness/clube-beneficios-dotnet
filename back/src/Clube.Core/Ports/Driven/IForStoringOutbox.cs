using Clube.Core.Domain.Vouchers;

namespace Clube.Core.Ports.Driven;

/// <summary>
/// Driven port: the relay's view of the outbox. Messages are written by <see cref="IForStoringMemberships.Enqueue"/>,
/// in the same unit of work as the posting they describe; this side only reads what is due and records each attempt.
/// </summary>
public interface IForStoringOutbox
{
    /// <summary>Pending messages whose next attempt is at or before <paramref name="now"/>, oldest due first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<OutboxMessage>> FindDueAsync(DateTimeOffset now, int limit, CancellationToken ct);

    /// <summary>Persists the message's status, attempts, next attempt and last error right away, one message at a time.</summary>
    Task RecordAttemptAsync(OutboxMessage message, CancellationToken ct);
}
