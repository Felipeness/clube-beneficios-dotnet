using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Clube.Adapters.DevTools;

// Development tooling as its own driving adapter, apart from Clube.Adapters.Http: the public API has no way to mention
// it, and removing the project's two calls from the Host removes every dev route, service and CORS rule at once.
// Both entry points refuse to run outside Development, so a wrong call in the Host fails the boot instead of exposing
// anonymous token minting and fake payments.
public static class DevToolsAdapter
{
    public const string CorsPolicy = "dev-tools";

    // Origins of the Expo web build; the native app does not go through CORS at all.
    private static readonly string[] ExpoOrigins = ["http://localhost:8081", "http://localhost:19006"];

    public static IServiceCollection AddDevToolsAdapter(this IServiceCollection services, IHostEnvironment environment)
    {
        RequireDevelopment(environment);
        services.AddSingleton(new DevClock(TimeProvider.System));
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(provider => provider.GetRequiredService<DevClock>()));
        services.AddSingleton<DevTokenIssuer>();
        services.AddScoped<PixWebhookLoopback>();
        services.AddScoped<PixSimulator>();
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(ExpoOrigins)
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")
            .AllowAnyMethod()));
        return services;
    }

    // Before UseHttpAdapter: CORS answers the browser's preflight before authentication would reject it for lacking a token.
    public static WebApplication UseDevTools(this WebApplication app)
    {
        RequireDevelopment(app.Environment);
        app.UseCors(CorsPolicy);
        app.MapDevEndpoints();
        return app;
    }

    private static void RequireDevelopment(IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("The development tools only run in the Development environment.");
    }
}
