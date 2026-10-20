using Clube.Domain.Memberships;

namespace Clube.Application.Memberships;

public interface IMembershipRepository
{
    Task<Membership?> FindAsync(MembershipId id, CancellationToken ct);

    void Add(Membership membership);

    /// <summary>Returns false when another write won the race (stale balance or duplicate holder).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);
}
