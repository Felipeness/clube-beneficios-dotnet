using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Clube.EndToEnd.Tests;

// Each factory gets its own in-memory SQLite database, kept alive by one open connection
// because SQLite drops a shared in-memory database when its last connection closes.
public sealed class ClubeApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString = $"Data Source=clube-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection keepAlive;

    public ClubeApiFactory()
    {
        keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
    }

    // Talks to the database behind the API's back, to prove rules that live in the schema itself.
    public int ExecuteSql(string sql)
    {
        using var command = keepAlive.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    // The ledger rules depend on "now", so tests own the clock instead of reading the machine's.
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-10-20T15:00:00Z"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Clube", connectionString);
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) keepAlive.Dispose();
        base.Dispose(disposing);
    }
}
