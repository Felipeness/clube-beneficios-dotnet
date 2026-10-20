using System.Diagnostics;
using Clube.Core.Domain.Vouchers;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Application;

// Delivers what the ledger already committed to the partner, at least once. Each message is settled and recorded on its own,
// so a partner failure on one never holds back or rolls back the others.
public sealed class OutboxDispatcher(IForStoringOutbox outbox, IForIssuingVouchers vouchers, TimeProvider clock) : IForRelayingTheOutbox
{
    public const int BatchSize = 20;
    public const int MaxAttempts = 6;
    public const double MaxJitter = 0.2;
    public static readonly TimeSpan PartnerTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);

    private const int MaxErrorLength = 500;

    public async Task<IReadOnlyList<OutboxMessage>> DispatchBatchAsync(CancellationToken ct)
    {
        var due = await outbox.FindDueAsync(clock.GetUtcNow(), BatchSize, ct);
        var settled = new List<OutboxMessage>(due.Count);
        // ponytail: one message at a time; bounded parallelism once a batch takes longer than the poll interval
        foreach (OutboxMessage message in due) settled.Add(await SettleAsync(message, ct));
        return settled;
    }

    // Exponential from FirstRetryDelay, stretched by up to MaxJitter so messages that failed together do not retry together.
    public static TimeSpan RetryDelay(int failedAttempts) =>
        FirstRetryDelay * Math.Pow(2, failedAttempts - 1) * (1 + (Random.Shared.NextDouble() * MaxJitter));

    private async Task<OutboxMessage> SettleAsync(OutboxMessage message, CancellationToken ct)
    {
        var settled = await AttemptAsync(message, ct);
        await outbox.RecordAttemptAsync(settled, ct);
        return settled;
    }

    // The partner is outside our control: whatever it throws means "not delivered yet", never "stop the relay".
    // Shutdown (ct) is the one failure that is not the partner's and leaves the message untouched.
    private async Task<OutboxMessage> AttemptAsync(OutboxMessage message, CancellationToken ct)
    {
        try
        {
            await DeliverWithTimeoutAsync(message.Event, ct);
            return message.Delivered();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed(message, $"partner did not answer within {PartnerTimeout.TotalSeconds}s");
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            return Failed(message, Describe(failure));
        }
    }

    private async Task DeliverWithTimeoutAsync(VoucherEvent voucherEvent, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(PartnerTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        await DeliverAsync(voucherEvent, linked.Token);
    }

    private Task DeliverAsync(VoucherEvent voucherEvent, CancellationToken ct) => voucherEvent switch
    {
        RewardRedeemed redeemed => vouchers.IssueAsync(new VoucherRequest(redeemed.TransactionId, redeemed.MembershipId, redeemed.RewardId), ct),
        RedemptionReversed reversed => vouchers.CancelAsync(reversed.RedemptionTransactionId, ct),
        _ => throw new UnreachableException($"No delivery for {voucherEvent.GetType().Name}"),
    };

    private OutboxMessage Failed(OutboxMessage message, string error)
    {
        int failedAttempts = message.Attempts + 1;
        if (failedAttempts >= MaxAttempts) return message.DeadLettered(error);

        return message.RetryAt(clock.GetUtcNow() + RetryDelay(failedAttempts), error);
    }

    private static string Describe(Exception failure)
    {
        string description = $"{failure.GetType().Name}: {failure.Message}";
        return description.Length <= MaxErrorLength ? description : description[..MaxErrorLength];
    }
}
