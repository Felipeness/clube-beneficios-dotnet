using Clube.Core.Domain.Reconciliation;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clube.Adapters.Jobs;

// Driving adapter on a timer: once a day, reconciles yesterday's Pix (Sao Paulo) against the PSP. Reading is all it does, so
// running it again after a restart is harmless; it only repeats the logs.
public sealed partial class ReconciliationWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReconciliationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(RunInterval, clock, stoppingToken);
        }
    }

    // A failing run (PSP down, database down) is logged and retried tomorrow: the worker must outlive it.
    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<IForReconcilingWithThePsp>();
            LogReport(await reconciliation.RunAsync(ClubeCalendar.DayOf(clock.GetUtcNow()).AddDays(-1), ct));
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            LogRunFailed(failure);
        }
    }

    private void LogReport(ReconciliationReport report)
    {
        string day = report.Day.ToString("yyyy-MM-dd");
        LogRunFinished(day, report.SettledAtPsp, report.CreditedInLedger, report.Divergences.Count);
        foreach (Divergence divergence in report.Divergences)
            LogDivergence(day, divergence.Kind, divergence.EndToEndId.Value, divergence.PspAmount?.Cents, divergence.LedgerAmount?.Cents);
        if (report.MissingInLedger > 0) LogMoneyReceivedNotCredited(day, report.MissingInLedger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pix reconciliation of {Day}: {SettledAtPsp} settled at the PSP, {CreditedInLedger} credited in the ledger, {Divergences} divergences")]
    private partial void LogRunFinished(string day, int settledAtPsp, int creditedInLedger, int divergences);

    // endToEndId and cents only, never the holder: the id leads to the membership through the ledger.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Pix reconciliation of {Day}: {Kind} for {EndToEndId} (PSP {PspCents} cents, ledger {LedgerCents} cents)")]
    private partial void LogDivergence(string day, DivergenceKind kind, string endToEndId, long? pspCents, long? ledgerCents);

    // Pages someone: members paid and got nothing until support credits them or sends the Pix back.
    [LoggerMessage(Level = LogLevel.Error, Message = "Pix reconciliation of {Day}: {Missing} Pix settled at the PSP were never credited in the ledger")]
    private partial void LogMoneyReceivedNotCredited(string day, int missing);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pix reconciliation run failed; retrying on the next day")]
    private partial void LogRunFailed(Exception failure);
}
