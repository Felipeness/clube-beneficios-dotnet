using Clube.Core.Domain.Memberships;
using Microsoft.EntityFrameworkCore;

namespace Clube.Adapters.Sqlite;

internal sealed class ClubeDbContext(DbContextOptions<ClubeDbContext> options) : DbContext(options)
{
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<LedgerTransactionRow> LedgerTransactions => Set<LedgerTransactionRow>();
    public DbSet<LedgerEntryRow> LedgerEntries => Set<LedgerEntryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MembershipConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerEntryConfiguration());
    }
}
