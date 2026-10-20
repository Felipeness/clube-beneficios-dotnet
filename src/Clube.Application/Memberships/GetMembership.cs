using Clube.Domain.Memberships;
using Clube.Domain.Shared;

namespace Clube.Application.Memberships;

public sealed class GetMembership(IMembershipRepository memberships)
{
    public async Task<Result<MembershipView>> ExecuteAsync(MembershipId id, CancellationToken ct)
    {
        var membership = await memberships.FindAsync(id, ct);
        return membership is null ? MembershipErrors.NotFound : MembershipView.From(membership);
    }
}
