using Clube.Application.Memberships;
using Clube.Domain.Memberships;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clube.Infrastructure.Persistence;

internal sealed class MembershipRepository(ClubeDbContext db) : IMembershipRepository
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
