using Clube.Domain.Memberships;
using Clube.Domain.Shared;

namespace Clube.Application.Memberships;

public sealed class RedeemReward(IMembershipRepository memberships)
{
    public async Task<Result<MembershipView>> ExecuteAsync(MembershipId id, string rewardId, CancellationToken ct)
    {
        if (!RewardCatalog.Find(rewardId).TryGetValue(out var reward, out var unknownReward)) return unknownReward;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        if (!membership.Redeem(reward).TryGetValue(out _, out var rejected)) return rejected;

        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.ConcurrentUpdate;
        return MembershipView.From(membership);
    }
}
