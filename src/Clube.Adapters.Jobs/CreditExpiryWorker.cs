using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clube.Adapters.Jobs;

// Driving adapter on a timer: once a day, asks the hexagon to expire credit up to this month's cutoff. Running daily on a
// monthly key is deliberate: the first run of a month does the work, later ones retry whoever failed and post nothing twice.
public sealed partial class CreditExpiryWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<CreditExpiryWorker> logger) : BackgroundService
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

    // A failing run (database down, a bug) is logged and retried tomorrow: the worker must outlive it.
    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var expiry = scope.ServiceProvider.GetRequiredService<IForExpiringCredits>();
            LogReport(await expiry.RunAsync(CreditExpiry.CutoffAt(clock.GetUtcNow()), ct));
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            LogRunFailed(failure);
        }
    }

    private void LogReport(CreditExpiryReport report)
    {
        string cutoff = report.Cutoff.ToString();
        LogRunFinished(cutoff, report.Expired, report.ExpiredCents, report.Failures.Count);
        foreach (MemberExpiryFailure failure in report.Failures) LogMemberFailed(failure.Member.Value, cutoff, failure.Error);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Credit expiry up to {Cutoff} expired {Expired} members, {ExpiredCents} cents in total, {Failed} failed")]
    private partial void LogRunFinished(string cutoff, int expired, long expiredCents, int failed);

    // Ids only, never the holder's CPF. Not an ERROR: tomorrow's run retries the member under the same key.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Credit expiry up to {Cutoff} failed for membership {MembershipId}: {Error}")]
    private partial void LogMemberFailed(Guid membershipId, string cutoff, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Credit expiry run failed; retrying on the next day")]
    private partial void LogRunFailed(Exception failure);
}
