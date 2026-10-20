using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driven;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clube.Adapters.Sqlite;

// Driven adapter: fulfils the storage port with EF Core + SQLite.
internal sealed partial class SqliteMembershipStore(ClubeDbContext db, ILogger<SqliteMembershipStore> logger) : IForStoringMemberships
{
    private const int SqliteUniqueConstraintViolation = 2067;

    // Logged only after the commit succeeds, so a rejected or retried posting never claims to have happened.
    private readonly List<LedgerTransaction> appended = [];

    public Task<Membership?> FindAsync(MembershipId id, CancellationToken ct) =>
        db.Memberships.FirstOrDefaultAsync(membership => membership.Id == id, ct);

    public async Task<Money> BalanceOfAsync(LedgerAccount account, CancellationToken ct) =>
        Money.FromCents(await db.LedgerEntries
            .Where(entry => entry.Account == account.Value)
            .SumAsync(entry => entry.SignedCents, ct));

    public async Task<StatementSlice> StatementOfAsync(LedgerAccount account, StatementPage page, CancellationToken ct)
    {
        long before = page.After?.Position ?? long.MaxValue;
        // Ordered by entry id (insertion order): SQLite cannot ORDER BY a DateTimeOffset column through EF Core.
        // One row beyond the limit tells whether another page exists.
        var rows = await db.LedgerEntries
            .Where(entry => entry.Account == account.Value && entry.Id < before)
            .Join(db.LedgerTransactions, entry => entry.TransactionId, transaction => transaction.Id,
                (entry, transaction) => new { entry.Id, TransactionId = transaction.Id, transaction.Description, entry.SignedCents, transaction.PostedAt, transaction.ActorKind, transaction.ActorId })
            .OrderByDescending(row => row.Id)
            .Take(page.Limit + 1)
            .ToListAsync(ct);

        return StatementSlice.From([.. rows.Select(row => new PositionedLine(
            row.Id,
            new StatementLine(row.TransactionId, row.Description, row.SignedCents, row.PostedAt, Enum.Parse<ActorKind>(row.ActorKind), row.ActorId)))], page);
    }

    public Task<string?> FindPostedFingerprintAsync(IdempotencyKey key, CancellationToken ct) =>
        db.LedgerTransactions
            .Where(transaction => transaction.IdempotencyKey == key.Value)
            .Select(transaction => (string?)transaction.Fingerprint)
            .SingleOrDefaultAsync(ct);

    public async Task<PostedTransaction?> FindTransactionAsync(Guid id, CancellationToken ct) =>
        ToPosted(await db.LedgerTransactions.Include(transaction => transaction.Entries).SingleOrDefaultAsync(transaction => transaction.Id == id, ct));

    public async Task<PostedTransaction?> FindTransactionByReferenceAsync(PaymentReference reference, CancellationToken ct) =>
        ToPosted(await db.LedgerTransactions.Include(transaction => transaction.Entries).SingleOrDefaultAsync(transaction => transaction.ExternalReference == reference.Value, ct));

    private static PostedTransaction? ToPosted(LedgerTransactionRow? row) =>
        row is null ? null : new PostedTransaction(row.Id, row.Description, [.. row.Entries.Select(ToEntry)], row.ReversesTransactionId);

    private static LedgerEntry ToEntry(LedgerEntryRow row) => new(
        LedgerAccount.FromStored(row.Account),
        Money.FromCents(Math.Abs(row.SignedCents)),
        row.SignedCents < 0 ? EntryDirection.Debit : EntryDirection.Credit);

    public void Add(Membership membership) => db.Memberships.Add(membership);

    public void Append(LedgerTransaction transaction)
    {
        appended.Add(transaction);
        db.LedgerTransactions.Add(ToRow(transaction));
    }

    private static LedgerTransactionRow ToRow(LedgerTransaction transaction) => new()
    {
        Id = transaction.Id,
        IdempotencyKey = transaction.Header.Key.Value,
        Fingerprint = transaction.Header.Fingerprint,
        Description = transaction.Header.Description,
        PostedAt = transaction.Header.PostedAt,
        ActorKind = transaction.Header.Caller.Actor.Kind.ToString(),
        ActorId = transaction.Header.Caller.Actor.Id,
        CorrelationId = transaction.Header.Caller.CorrelationId,
        ReversesTransactionId = transaction.Header.ReversesTransactionId,
        ExternalReference = transaction.Header.ExternalReference?.Value,
        Entries = [.. transaction.Entries.Select(entry => new LedgerEntryRow
        {
            TransactionId = transaction.Id,
            Account = entry.Account.Value,
            SignedCents = entry.SignedCents,
        })],
    };

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            appended.ForEach(LogPosted);
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
        finally
        {
            appended.Clear();
        }
    }

    private void LogPosted(LedgerTransaction transaction) => LogTransactionPosted(
        transaction.Id, transaction.Header.Key.Value, transaction.Header.Caller.Actor.Kind, transaction.Header.Caller.CorrelationId);

    // No CPF, no actor id beyond its kind: the transaction id leads to everything else.
    [LoggerMessage(Level = LogLevel.Information, Message = "Ledger transaction {TransactionId} posted with key {IdempotencyKey} by {ActorKind} (correlation {CorrelationId})")]
    private partial void LogTransactionPosted(Guid transactionId, string idempotencyKey, ActorKind actorKind, string correlationId);
}
