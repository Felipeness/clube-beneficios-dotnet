using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driven;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clube.Adapters.Sqlite;

// Driven adapter: fulfils the storage port with EF Core + SQLite.
internal sealed class SqliteMembershipStore(ClubeDbContext db) : IForStoringMemberships
{
    private const int SqliteUniqueConstraintViolation = 2067;

    public Task<Membership?> FindAsync(MembershipId id, CancellationToken ct) =>
        db.Memberships.FirstOrDefaultAsync(membership => membership.Id == id, ct);

    public async Task<Money> BalanceOfAsync(LedgerAccount account, CancellationToken ct) =>
        Money.FromCents(await db.LedgerEntries
            .Where(entry => entry.Account == account.Value)
            .SumAsync(entry => entry.SignedCents, ct));

    public async Task<IReadOnlyList<StatementLine>> StatementOfAsync(LedgerAccount account, int limit, CancellationToken ct)
    {
        // Ordered by entry id (insertion order): SQLite cannot ORDER BY a DateTimeOffset column through EF Core.
        var rows = await db.LedgerEntries
            .Where(entry => entry.Account == account.Value)
            .Join(db.LedgerTransactions, entry => entry.TransactionId, transaction => transaction.Id,
                (entry, transaction) => new { entry.Id, TransactionId = transaction.Id, transaction.Description, entry.SignedCents, transaction.PostedAt })
            .OrderByDescending(row => row.Id)
            .Take(limit)
            .ToListAsync(ct);

        return [.. rows.Select(row => new StatementLine(row.TransactionId, row.Description, row.SignedCents, row.PostedAt))];
    }

    public Task<string?> FindPostedFingerprintAsync(IdempotencyKey key, CancellationToken ct) =>
        db.LedgerTransactions
            .Where(transaction => transaction.IdempotencyKey == key.Value)
            .Select(transaction => (string?)transaction.Fingerprint)
            .SingleOrDefaultAsync(ct);

    public void Add(Membership membership) => db.Memberships.Add(membership);

    public void Append(LedgerTransaction transaction) => db.LedgerTransactions.Add(new LedgerTransactionRow
    {
        Id = transaction.Id,
        IdempotencyKey = transaction.Header.Key.Value,
        Fingerprint = transaction.Header.Fingerprint,
        Description = transaction.Header.Description,
        PostedAt = transaction.Header.PostedAt,
        Entries = [.. transaction.Entries.Select(entry => new LedgerEntryRow
        {
            TransactionId = transaction.Id,
            Account = entry.Account.Value,
            SignedCents = entry.SignedCents,
        })],
    });

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteUniqueConstraintViolation })
        {
            return false;
        }
    }
}
