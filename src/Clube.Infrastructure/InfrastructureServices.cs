using Clube.Application.Memberships;
using Clube.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddClubeInfrastructure(this IServiceCollection services, string connectionString) =>
        services
            .AddDbContext<ClubeDbContext>(options => options.UseSqlite(connectionString))
            .AddScoped<IMembershipRepository, MembershipRepository>();

    // ponytail: EnsureCreated keeps no schema history; switch to EF migrations before the first schema change ships
    public static async Task EnsureClubeDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ClubeDbContext>().Database.EnsureCreatedAsync();
    }
}
