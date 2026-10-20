using Microsoft.Extensions.DependencyInjection;

namespace Clube.Adapters.Jobs;

// Scheduled driving adapters: the ledger's own housekeeping, apart from Partners, which talks to the outside world.
public static class JobsAdapter
{
    public static IServiceCollection AddJobsAdapter(this IServiceCollection services) =>
        services
            .AddHostedService<CreditExpiryWorker>()
            .AddHostedService<ReconciliationWorker>();
}
