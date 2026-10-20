using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Clube", connectionString);

    protected override void Dispose(bool disposing)
    {
        if (disposing) keepAlive.Dispose();
        base.Dispose(disposing);
    }
}
