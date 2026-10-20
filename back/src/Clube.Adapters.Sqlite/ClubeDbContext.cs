using Clube.Core.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Clube.Adapters.Sqlite;

internal sealed class ClubeDbContext(DbContextOptions<ClubeDbContext> options, PiiProtector pii) : DbContext(options)
{
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<LedgerTransactionRow> LedgerTransactions => Set<LedgerTransactionRow>();
    public DbSet<LedgerEntryRow> LedgerEntries => Set<LedgerEntryRow>();
    public DbSet<OutboxMessageRow> OutboxMessages => Set<OutboxMessageRow>();

    public PiiProtector Pii => pii;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MembershipConfiguration(pii));
        modelBuilder.ApplyConfiguration(new LedgerTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}

// EF builds the model once per context type and caches it process-wide, and the CPF converter inside it closes over the keys.
// Keying the cache on the protector too means a second host in the same process (other keys) builds its own model instead of
// silently encrypting with the first host's keys. One host has one protector, so production still builds the model once.
internal sealed class PiiModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is ClubeDbContext clube ? (context.GetType(), clube.Pii, designTime) : (context.GetType(), designTime);
}
