using Clube.Core.Domain.Memberships;

namespace Clube.Core.Ports.Driven;

/// <summary>
/// Driven port: the reward partner (cinema, perfumery...). Calls are redelivered after a timeout or a lost
/// acknowledgement, so the partner must treat a repeated <c>IdempotencyKey</c> as the same voucher.
/// A thrown exception means "not done, try again later".
/// </summary>
public interface IForIssuingVouchers
{
    Task IssueAsync(VoucherRequest request, CancellationToken ct);

    /// <summary>Cancels the voucher issued under <paramref name="voucherKey"/> (the redemption's idempotency key).</summary>
    Task CancelAsync(Guid voucherKey, CancellationToken ct);
}

public sealed record VoucherRequest(Guid IdempotencyKey, MembershipId MembershipId, string RewardId);
