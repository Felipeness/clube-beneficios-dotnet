using Clube.Core.Ports.Driven;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clube.Adapters.Sqlite;

public static class SqliteAdapter
{
    public static IServiceCollection AddSqliteAdapter(this IServiceCollection services, string connectionString)
    {
        // Fails the boot instead of storing CPFs under a missing, short or shared key. Development reads its dev-only keys from
        // appsettings.Development.json, so there is no environment where the column is written in the clear.
        services.AddOptions<PiiOptions>()
            .BindConfiguration(PiiOptions.Section)
            .Validate(pii => pii.AreValid(), PiiOptions.Invalid)
            .ValidateOnStart();
        return services
            .AddSingleton(provider => PiiProtector.From(provider.GetRequiredService<IOptions<PiiOptions>>()))
            .AddDbContext<ClubeDbContext>(options => options
                .UseSqlite(connectionString)
                .ReplaceService<IModelCacheKeyFactory, PiiModelCacheKeyFactory>())
            .AddScoped<IForStoringMemberships, SqliteMembershipStore>()
            .AddScoped<IForStoringOutbox, SqliteOutboxStore>();
    }

    // Development and tests migrate on boot. Elsewhere the deploy applies the migrations as its own step (idempotent script or
    // bundle) and the host refuses to start against a database that is behind, instead of racing other instances to alter it.
    public static async Task PrepareSqliteSchemaAsync(this IServiceProvider services, bool migrateOnBoot)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubeDbContext>();
        if (migrateOnBoot)
        {
            await db.Database.MigrateAsync();
            return;
        }
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException($"Database is behind the code, apply the pending migrations before starting: {string.Join(", ", pending)}.");
    }
}
