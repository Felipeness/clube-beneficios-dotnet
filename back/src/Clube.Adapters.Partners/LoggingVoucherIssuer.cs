using Clube.Core.Ports.Driven;
using Microsoft.Extensions.Logging;

namespace Clube.Adapters.Partners;

// Stand-in for the real partner API until there is a contract: it only logs, and always succeeds.
// The real adapter goes here, with an HttpClient bound to the dispatcher's token and IdempotencyKey sent as a header.
internal sealed partial class LoggingVoucherIssuer(ILogger<LoggingVoucherIssuer> logger) : IForIssuingVouchers
{
    public Task IssueAsync(VoucherRequest request, CancellationToken ct)
    {
        LogIssued(request.IdempotencyKey, request.RewardId, request.MembershipId.Value);
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid voucherKey, CancellationToken ct)
    {
        LogCancelled(voucherKey);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Partner voucher {VoucherKey} issued for reward {RewardId} to membership {MembershipId}")]
    private partial void LogIssued(Guid voucherKey, string rewardId, Guid membershipId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Partner voucher {VoucherKey} cancelled")]
    private partial void LogCancelled(Guid voucherKey);
}
