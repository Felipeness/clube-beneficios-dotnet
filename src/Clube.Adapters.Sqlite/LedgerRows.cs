using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clube.Adapters.Sqlite;

// Persistence shape of the ledger, kept apart from the domain types: one signed column per entry
// makes the balance a plain SUM over an index, and the domain never sees these rows.
internal sealed class LedgerTransactionRow
{
    public Guid Id { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Fingerprint { get; init; }
    public required string Description { get; init; }
    public DateTimeOffset PostedAt { get; init; }
    // The same instant as unix milliseconds (UTC), because SQLite cannot compare a DateTimeOffset column through EF Core.
    // PostedAt stays as the readable value with its offset; this one exists only for range filters like the expiry cutoff.
    public long PostedAtUnixMs { get; init; }
    public required string ActorKind { get; init; }
    public string? ActorId { get; init; }
    public required string CorrelationId { get; init; }
    public Guid? ReversesTransactionId { get; init; }
    public string? ExternalReference { get; init; }
    public List<LedgerEntryRow> Entries { get; init; } = [];
}

internal sealed class LedgerEntryRow
{
    public long Id { get; init; }
    public Guid TransactionId { get; init; }
    public required string Account { get; init; }
    public long SignedCents { get; init; }
}

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransactionRow>
{
    public void Configure(EntityTypeBuilder<LedgerTransactionRow> builder)
    {
        builder.ToTable("ledger_transactions");
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.IdempotencyKey).HasMaxLength(160);
        builder.Property(transaction => transaction.Fingerprint).HasMaxLength(160);
        builder.Property(transaction => transaction.ActorKind).HasMaxLength(32);
        builder.Property(transaction => transaction.ActorId).HasMaxLength(64);
        builder.Property(transaction => transaction.CorrelationId).HasMaxLength(64);
        builder.HasIndex(transaction => transaction.IdempotencyKey).IsUnique();
        builder.Property(transaction => transaction.ExternalReference).HasMaxLength(64);
        builder.HasIndex(transaction => transaction.CorrelationId);
        // A provider payment is posted once, ever; SQLite lets the many internal postings without one share NULL.
        builder.HasIndex(transaction => transaction.ExternalReference).IsUnique();
        builder.HasMany(transaction => transaction.Entries).WithOne().HasForeignKey(entry => entry.TransactionId);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntryRow>
{
    public void Configure(EntityTypeBuilder<LedgerEntryRow> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Account).HasMaxLength(64);
        builder.HasIndex(entry => entry.Account);
    }
}
