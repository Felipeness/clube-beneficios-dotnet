using Clube.Core.Ports.Driven;
using Clube.Core.Domain.Memberships;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clube.Adapters.Sqlite;

// Driven adapter: fulfils the storage port with EF Core + SQLite.
internal sealed class SqliteMembershipStore(ClubeDbContext db) : IForStoringMemberships
{
    private const int SqliteUniqueConstraintViolation = 2067;

    public Task<Membership?> FindAsync(MembershipId id, CancellationToken ct) =>
        db.Memberships.FirstOrDefaultAsync(membership => membership.Id == id, ct);

    public void Add(Membership membership) => db.Memberships.Add(membership);

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteUniqueConstraintViolation })
        {
            return false;
        }
    }
}
