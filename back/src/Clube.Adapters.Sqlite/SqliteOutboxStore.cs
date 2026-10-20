using Clube.Core.Domain.Vouchers;
using Clube.Core.Ports.Driven;
using Microsoft.EntityFrameworkCore;

namespace Clube.Adapters.Sqlite;

// Driven adapter for the relay side of the outbox. Single relay instance assumed: two would read the same due rows,
// which is safe for the partner (idempotency key) but wasteful; see the README for what several instances need.
internal sealed class SqliteOutboxStore(ClubeDbContext db) : IForStoringOutbox
{
    private static readonly string PendingStatus = nameof(OutboxStatus.Pending);

    public async Task<IReadOnlyList<OutboxMessage>> FindDueAsync(DateTimeOffset now, int limit, CancellationToken ct)
    {
        long nowUnixMs = now.ToUnixTimeMilliseconds();
        var rows = await db.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Status == PendingStatus && message.NextAttemptAtUnixMs <= nowUnixMs)
            .OrderBy(message => message.NextAttemptAtUnixMs)
            .Take(limit)
            .ToListAsync(ct);
        return [.. rows.Select(row => row.ToMessage())];
    }

    public Task RecordAttemptAsync(OutboxMessage message, CancellationToken ct)
    {
        var row = OutboxMessageRow.From(message);
        return db.OutboxMessages
            .Where(stored => stored.Id == row.Id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(stored => stored.Status, row.Status)
                .SetProperty(stored => stored.Attempts, row.Attempts)
                .SetProperty(stored => stored.NextAttemptAtUnixMs, row.NextAttemptAtUnixMs)
                .SetProperty(stored => stored.LastError, row.LastError), ct);
    }
}
