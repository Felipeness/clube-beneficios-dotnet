using Clube.Domain.Memberships;
using Clube.Domain.Shared;

namespace Clube.Application.Memberships;

public sealed class SubscribeMember(IMembershipRepository memberships, TimeProvider clock)
{
    public async Task<Result<MembershipView>> ExecuteAsync(string rawCpf, CancellationToken ct)
    {
        if (!Cpf.Parse(rawCpf).TryGetValue(out var holder, out var invalidCpf)) return invalidCpf;

        var membership = Membership.Subscribe(holder, clock.GetUtcNow());
        memberships.Add(membership);

        // The unique index on holder is the source of truth; a pre-check would still race.
        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.AlreadySubscribed;
        return MembershipView.From(membership);
    }
}
