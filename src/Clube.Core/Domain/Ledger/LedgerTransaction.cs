using Clube.Core.Domain.Memberships;
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

public sealed record TransactionHeader(
    IdempotencyKey Key,
    string Fingerprint,
    string Description,
    DateTimeOffset PostedAt,
    CallerContext Caller,
    Guid? ReversesTransactionId = null,
    PaymentReference? ExternalReference = null);

// A posted transaction as read back from the store: enough to mirror it, without exposing a way to rebuild or edit one.
public sealed record PostedTransaction(Guid Id, string Description, IReadOnlyList<LedgerEntry> Entries, Guid? ReversesTransactionId)
{
    public bool Touches(LedgerAccount account) => Entries.Any(entry => entry.Account == account);

    public MembershipId? Member => Entries.Select(entry => entry.Account.Owner).FirstOrDefault(owner => owner is not null);
}

public sealed record StatementLine(Guid TransactionId, string Description, long AmountCents, DateTimeOffset PostedAt, ActorKind ActorKind, string? ActorId);

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

    // Full reversal only. The key is derived from the original, so reversing twice is the same request, and a
    // reversal of a reversal is refused: the original is the one thing a correction can point back to.
    public static Result<LedgerTransaction> Reverse(PostedTransaction original, PostingContext context)
    {
        if (original.ReversesTransactionId is not null) return LedgerErrors.ReversalOfReversal;

        var key = IdempotencyKey.ForReversal(original.Id);
        var header = new TransactionHeader(key, key.Value, $"Reversal of {original.Description}", context.At, context.Caller, original.Id);
        return Post(header, [.. original.Entries.Select(Mirror)]);
    }

    private static LedgerEntry Mirror(LedgerEntry entry) => entry with
    {
        Direction = entry.Direction == EntryDirection.Debit ? EntryDirection.Credit : EntryDirection.Debit,
    };

    public static Result<LedgerTransaction> Transfer(LedgerAccount from, LedgerAccount to, Money amount, TransactionHeader header) =>
        Post(header, [new LedgerEntry(from, amount, EntryDirection.Debit), new LedgerEntry(to, amount, EntryDirection.Credit)]);
}
