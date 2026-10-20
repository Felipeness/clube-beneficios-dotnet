using Clube.Core.Ports.Driven;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.Adapters.Sqlite;

public static class SqliteAdapter
{
    public static IServiceCollection AddSqliteAdapter(this IServiceCollection services, string connectionString) =>
        services
            .AddDbContext<ClubeDbContext>(options => options.UseSqlite(connectionString))
            .AddScoped<IForStoringMemberships, SqliteMembershipStore>();

    // ponytail: EnsureCreated keeps no schema history; switch to EF migrations before the first schema change ships
    public static async Task EnsureSqliteSchemaAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ClubeDbContext>().Database.EnsureCreatedAsync();
    }
}
