using Clube.Core.Ports.Driven;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    public static IServiceCollection AddSqliteAdapter(this IServiceCollection services, string connectionString) =>
        services
            .AddDbContext<ClubeDbContext>(options => options.UseSqlite(connectionString))
            .AddScoped<IForStoringMemberships, SqliteMembershipStore>()
            .AddScoped<IForStoringOutbox, SqliteOutboxStore>();

    // ponytail: EnsureCreated keeps no schema history; switch to EF migrations before the first schema change ships
    public static async Task EnsureSqliteSchemaAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubeDbContext>();
        await db.Database.EnsureCreatedAsync();
        foreach (string trigger in AppendOnlyTriggers) await db.Database.ExecuteSqlRawAsync(trigger);
    }
}
