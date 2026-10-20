using Clube.Domain.Memberships;
using Microsoft.EntityFrameworkCore;

namespace Clube.Infrastructure.Persistence;

public sealed class ClubeDbContext(DbContextOptions<ClubeDbContext> options) : DbContext(options)
{
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new MembershipConfiguration());
}
