using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Ledger;

public enum EntryDirection
{
    Debit,
    Credit,
}

public sealed record LedgerEntry(LedgerAccount Account, Money Amount, EntryDirection Direction)
{
    public long SignedCents => Direction == EntryDirection.Credit ? Amount.Cents : -Amount.Cents;
}

public sealed record TransactionHeader(IdempotencyKey Key, string Description, DateTimeOffset PostedAt);

public sealed record StatementLine(Guid TransactionId, string Description, long AmountCents, DateTimeOffset PostedAt);

// Immutable once posted: a mistake is fixed with a reversing transaction, never by editing or deleting.
public sealed class LedgerTransaction
{
    public Guid Id { get; } = Guid.CreateVersion7();
    public TransactionHeader Header { get; }
    public IReadOnlyList<LedgerEntry> Entries { get; }

    private LedgerTransaction(TransactionHeader header, IReadOnlyList<LedgerEntry> entries)
    {
        Header = header;
        Entries = entries;
    }

    public static Result<LedgerTransaction> Post(TransactionHeader header, IReadOnlyList<LedgerEntry> entries)
    {
        if (entries.Count < 2) return LedgerErrors.TooFewEntries;
        if (!entries.All(entry => entry.Amount.IsPositive)) return LedgerErrors.NonPositiveAmount;
        // Double entry: every centavo that leaves an account lands in another one.
        if (entries.Sum(entry => entry.SignedCents) != 0) return LedgerErrors.Unbalanced;

        return new LedgerTransaction(header, [.. entries]);
    }

    public static Result<LedgerTransaction> Transfer(LedgerAccount from, LedgerAccount to, Money amount, TransactionHeader header) =>
        Post(header, [new LedgerEntry(from, amount, EntryDirection.Debit), new LedgerEntry(to, amount, EntryDirection.Credit)]);
}
