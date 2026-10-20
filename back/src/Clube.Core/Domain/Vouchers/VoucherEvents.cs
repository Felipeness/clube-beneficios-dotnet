using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;

namespace Clube.Core.Domain.Vouchers;

// What the reward partner must hear about, recorded beside the posting that caused it. TransactionId is that posting.
public abstract record VoucherEvent(Guid TransactionId, MembershipId MembershipId, DateTimeOffset PostedAt);

public sealed record RewardRedeemed(Guid TransactionId, MembershipId MembershipId, DateTimeOffset PostedAt, string RewardId, long PriceCents)
    : VoucherEvent(TransactionId, MembershipId, PostedAt)
{
    public static RewardRedeemed Of(LedgerTransaction redemption, MembershipId member, Reward reward) =>
        new(redemption.Id, member, redemption.Header.PostedAt, reward.Id, reward.Price.Cents);
}

// The redemption's transaction id is the voucher's key at the partner, so it is what a cancellation points to.
public sealed record RedemptionReversed(Guid TransactionId, MembershipId MembershipId, DateTimeOffset PostedAt, Guid RedemptionTransactionId)
    : VoucherEvent(TransactionId, MembershipId, PostedAt)
{
    public static RedemptionReversed Of(LedgerTransaction reversal, MembershipId member, Guid redemptionTransactionId) =>
        new(reversal.Id, member, reversal.Header.PostedAt, redemptionTransactionId);
}

public enum OutboxStatus
{
    Pending,
    Delivered,
    DeadLettered,
}

// One delivery to the partner and how it is going. Not part of the ledger: it is the only record here that changes after it is written.
public sealed record OutboxMessage(Guid Id, VoucherEvent Event, OutboxStatus Status, int Attempts, DateTimeOffset NextAttemptAt, string? LastError)
{
    public static OutboxMessage For(VoucherEvent voucherEvent) =>
        new(Guid.CreateVersion7(), voucherEvent, OutboxStatus.Pending, 0, voucherEvent.PostedAt, null);

    public OutboxMessage Delivered() => this with { Status = OutboxStatus.Delivered, Attempts = Attempts + 1, LastError = null };

    public OutboxMessage RetryAt(DateTimeOffset nextAttemptAt, string error) => this with { Attempts = Attempts + 1, NextAttemptAt = nextAttemptAt, LastError = error };

    public OutboxMessage DeadLettered(string error) => this with { Status = OutboxStatus.DeadLettered, Attempts = Attempts + 1, LastError = error };
}
