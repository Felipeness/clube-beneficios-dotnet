using System.Text.Json;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Clube.Adapters.Http;

// Proves, on every probe, that the books balance. An orchestrator can call it without a token, so the answer is the status
// and the difference in cents, nothing else: no exception text, no account, no member.
internal sealed partial class LedgerBalanceHealthCheck(IForCheckingLedgerIntegrity ledger, ILogger<LedgerBalanceHealthCheck> logger) : IHealthCheck
{
    public const string Name = "ledger";
    public const string DifferenceKey = "differenceCents";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        long difference = await ledger.ImbalanceCentsAsync(ct);
        if (difference == 0) return HealthCheckResult.Healthy(data: DifferenceOf(0));

        LogLedgerOutOfBalance(difference);
        return HealthCheckResult.Unhealthy(data: DifferenceOf(difference));
    }

    private static Dictionary<string, object> DifferenceOf(long cents) => new() { [DifferenceKey] = cents };

    // Double entry is the one thing the ledger exists to guarantee; a nonzero sum means a row was written around the domain.
    [LoggerMessage(Level = LogLevel.Error, Message = "Ledger is out of balance: entries sum to {DifferenceCents} cents instead of zero")]
    private partial void LogLedgerOutOfBalance(long differenceCents);
}

internal static class LedgerHealthEndpoints
{
    public static IServiceCollection AddLedgerHealth(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<LedgerBalanceHealthCheck>(LedgerBalanceHealthCheck.Name, tags: [LedgerBalanceHealthCheck.Name], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    public static void MapHealth(this IEndpointRouteBuilder app)
    {
        // Liveness runs no check: it must stay cheap, while the ledger probe sums every entry.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ledger", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LedgerBalanceHealthCheck.Name),
            ResponseWriter = WriteStatusAndDifferenceAsync,
        }).AllowAnonymous();
    }

    private static Task WriteStatusAndDifferenceAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        object? difference = report.Entries.Values.Select(entry => entry.Data.GetValueOrDefault(LedgerBalanceHealthCheck.DifferenceKey)).FirstOrDefault(value => value is not null);
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status.ToString(), differenceCents = difference }, JsonSerializerOptions.Web));
    }
}
