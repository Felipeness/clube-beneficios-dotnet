using Clube.Core.Application;
using Clube.Core.Domain.Vouchers;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clube.Adapters.Partners;

// Driving adapter on a timer: keeps calling the relay port. Every delivery rule lives in OutboxDispatcher; this only loops,
// gives each batch its own scope (and DbContext), and logs what happened.
public sealed partial class OutboxWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int settled = await RelayBatchAsync(stoppingToken);
            // A full batch means more may be due right now; anything less means the outbox is caught up.
            if (settled < OutboxDispatcher.BatchSize) await Task.Delay(IdlePollInterval, clock, stoppingToken);
        }
    }

    // A failing batch (database down, a bug) is logged and retried on the next tick: the worker must outlive it.
    private async Task<int> RelayBatchAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var settled = await scope.ServiceProvider.GetRequiredService<IForRelayingTheOutbox>().DispatchBatchAsync(ct);
            foreach (OutboxMessage message in settled) LogSettled(message);
            return settled.Count;
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            LogBatchFailed(failure);
            return 0;
        }
    }

    private void LogSettled(OutboxMessage message)
    {
        string eventType = message.Event.GetType().Name;
        Guid transactionId = message.Event.TransactionId;
        switch (message.Status)
        {
            case OutboxStatus.Delivered: LogDelivered(message.Id, eventType, transactionId, message.Attempts); break;
            case OutboxStatus.DeadLettered: LogDeadLettered(message.Id, eventType, transactionId, message.Attempts, message.LastError); break;
            default: LogRetryScheduled(message.Id, eventType, transactionId, message.Attempts, message.NextAttemptAt, message.LastError); break;
        }
    }

    // Ids only, never the holder's CPF: the transaction id leads to the member and the statement.
    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} ({EventType}, transaction {TransactionId}) delivered on attempt {Attempts}")]
    private partial void LogDelivered(Guid messageId, string eventType, Guid transactionId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({EventType}, transaction {TransactionId}) failed attempt {Attempts}, retrying at {NextAttemptAt}: {LastError}")]
    private partial void LogRetryScheduled(Guid messageId, string eventType, Guid transactionId, int attempts, DateTimeOffset nextAttemptAt, string? lastError);

    // Pages someone: the ledger moved and the partner never heard of it, so support settles it by hand.
    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({EventType}, transaction {TransactionId}) dead-lettered after {Attempts} attempts: {LastError}")]
    private partial void LogDeadLettered(Guid messageId, string eventType, Guid transactionId, int attempts, string? lastError);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed; retrying on the next tick")]
    private partial void LogBatchFailed(Exception failure);
}
