using Clube.Adapters.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.EndToEnd.Tests;

public sealed class MigrationsTests(ClubeApiFactory factory) : IClassFixture<ClubeApiFactory>
{
    // Fails the build of the next developer who changes an entity or its mapping without running dotnet ef migrations add.
    [Fact]
    public void ModelHasNoChangesMissingFromMigrations()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubeDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task AppliesEveryMigrationAtBoot()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubeDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task RefusesToBootOutsideDevelopmentWithPendingMigrations()
    {
        await using var unmigrated = new ClubeApiFactory();
        using var production = unmigrated.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var refused = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("InitialSchema", refused.ToString());
    }
}
