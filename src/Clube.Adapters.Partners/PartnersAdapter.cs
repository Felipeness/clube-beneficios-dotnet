using Clube.Core.Ports.Driven;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.Adapters.Partners;

public static class PartnersAdapter
{
    public static IServiceCollection AddPartnersAdapter(this IServiceCollection services) =>
        services
            .AddSingleton<IForIssuingVouchers, LoggingVoucherIssuer>()
            .AddHostedService<OutboxWorker>();
}
