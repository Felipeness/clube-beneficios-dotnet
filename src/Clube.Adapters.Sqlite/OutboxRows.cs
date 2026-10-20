using System.Text.Json;
using Clube.Core.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clube.Adapters.Sqlite;

// Persistence shape of the outbox. The event travels as JSON tagged by its type name, so a new event is a new case
// in Deserialize instead of a schema change. The next attempt is unix milliseconds because SQLite cannot compare or
// order a DateTimeOffset column through EF Core. Unlike the ledger tables, rows here are updated (no triggers).
internal sealed class OutboxMessageRow
{
    public Guid Id { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public required string Status { get; init; }
    public int Attempts { get; init; }
    public long NextAttemptAtUnixMs { get; init; }
    public string? LastError { get; init; }

    public static OutboxMessageRow From(OutboxMessage message) => new()
    {
        Id = message.Id,
        EventType = message.Event.GetType().Name,
        Payload = JsonSerializer.Serialize(message.Event, message.Event.GetType()),
        Status = message.Status.ToString(),
        Attempts = message.Attempts,
        NextAttemptAtUnixMs = message.NextAttemptAt.ToUnixTimeMilliseconds(),
        LastError = message.LastError,
    };

    public OutboxMessage ToMessage() => new(
        Id,
        Deserialize(),
        Enum.Parse<OutboxStatus>(Status),
        Attempts,
        DateTimeOffset.FromUnixTimeMilliseconds(NextAttemptAtUnixMs),
        LastError);

    private VoucherEvent Deserialize() => EventType switch
    {
        nameof(RewardRedeemed) => (VoucherEvent?)JsonSerializer.Deserialize<RewardRedeemed>(Payload),
        nameof(RedemptionReversed) => JsonSerializer.Deserialize<RedemptionReversed>(Payload),
        _ => null,
    } ?? throw new InvalidOperationException($"Outbox message {Id} has an unreadable {EventType} payload.");
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageRow>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRow> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.EventType).HasMaxLength(64);
        builder.Property(message => message.Status).HasMaxLength(16);
        builder.Property(message => message.LastError).HasMaxLength(500);
        // The relay's only query: pending messages that are due, oldest first.
        builder.HasIndex(message => new { message.Status, message.NextAttemptAtUnixMs });
    }
}
