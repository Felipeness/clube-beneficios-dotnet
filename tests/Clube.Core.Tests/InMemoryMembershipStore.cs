using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driven;

namespace Clube.Core.Tests;

// Second driven adapter for the same port: lets the hexagon run with no database at all.
internal sealed class InMemoryMembershipStore : IForStoringMemberships
{
    private readonly Dictionary<MembershipId, Membership> saved = [];
    private readonly List<Membership> pending = [];

    public Task<Membership?> FindAsync(MembershipId id, CancellationToken ct) =>
        Task.FromResult(saved.GetValueOrDefault(id));

    public void Add(Membership membership) => pending.Add(membership);

    public Task<bool> TrySaveChangesAsync(CancellationToken ct)
    {
        var holderTaken = pending.Any(candidate => saved.Values.Any(existing => existing.Holder == candidate.Holder));
        if (!holderTaken) pending.ForEach(membership => saved[membership.Id] = membership);

        pending.Clear();
        return Task.FromResult(!holderTaken);
    }
}
