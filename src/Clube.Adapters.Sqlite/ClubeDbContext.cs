using Clube.Core.Domain.Memberships;
using Microsoft.EntityFrameworkCore;

namespace Clube.Adapters.Sqlite;

internal sealed class ClubeDbContext(DbContextOptions<ClubeDbContext> options) : DbContext(options)
{
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new MembershipConfiguration());
}
