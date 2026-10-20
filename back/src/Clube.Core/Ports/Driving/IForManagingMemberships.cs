using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Ports.Driving;

/// <summary>
/// Driving (primary) port: everything the outside world may ask of the hexagon about memberships.
/// HTTP, a CLI or a test harness are all driving adapters that call this and nothing else.
/// </summary>
public interface IForManagingMemberships
{
    /// <summary>The catalog a member can redeem from, with the final price already discounted. Public: it names no member.</summary>
    IReadOnlyList<RewardView> ListRewards();

    Task<Result<MembershipView>> SubscribeAsync(string cpf, CancellationToken ct);

    Task<Result<MembershipView>> GetAsync(MembershipId id, CancellationToken ct);

    Task<Result<StatementView>> GetStatementAsync(MembershipId id, StatementPage page, CancellationToken ct);

    /// <summary>Credits the period a confirmed payment pays for; a redelivery or a second payment for a paid period credits nothing.</summary>
    Task<Result<AllowanceCreditOutcome>> CreditMonthlyAllowanceAsync(MembershipId id, ReceivedPayment payment, CancellationToken ct);

    /// <summary>Reverses the credit posted for the refunded payment, found by its provider reference.</summary>
    Task<Result<PaymentRefundOutcome>> RefundPaymentAsync(string originalReference, CancellationToken ct);

    Task<Result<MembershipView>> RedeemRewardAsync(MembershipId id, string rewardId, string idempotencyKey, CancellationToken ct);

    Task<Result<MembershipView>> ReverseTransactionAsync(MembershipId id, Guid transactionId, CancellationToken ct);
}
