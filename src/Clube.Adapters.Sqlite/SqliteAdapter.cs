using Clube.Core.Ports.Driven;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clube.Adapters.Sqlite;

public static class SqliteAdapter
{
    // The database itself refuses to rewrite history, so append-only does not depend on every developer remembering it.
    private static readonly string[] AppendOnlyTriggers =
    [
        "CREATE TRIGGER IF NOT EXISTS ledger_entries_no_update BEFORE UPDATE ON ledger_entries BEGIN SELECT RAISE(ABORT, 'ledger is append-only'); END;",
        "CREATE TRIGGER IF NOT EXISTS ledger_entries_no_delete BEFORE DELETE ON ledger_entries BEGIN SELECT RAISE(ABORT, 'ledger is append-only'); END;",
        "CREATE TRIGGER IF NOT EXISTS ledger_transactions_no_update BEFORE UPDATE ON ledger_transactions BEGIN SELECT RAISE(ABORT, 'ledger is append-only'); END;",
        "CREATE TRIGGER IF NOT EXISTS ledger_transactions_no_delete BEFORE DELETE ON ledger_transactions BEGIN SELECT RAISE(ABORT, 'ledger is append-only'); END;",
    ];

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

    // ponytail: EnsureCreated keeps no schema history; switch to EF migrations before the first schema change ships
    public static async Task EnsureSqliteSchemaAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubeDbContext>();
        await db.Database.EnsureCreatedAsync();
        foreach (string trigger in AppendOnlyTriggers) await db.Database.ExecuteSqlRawAsync(trigger);
    }
}
