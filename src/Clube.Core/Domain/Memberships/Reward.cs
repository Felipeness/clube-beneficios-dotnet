using System.Collections.Frozen;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public sealed record Reward(string Id, string Name, Credits Cost);

public static class RewardCatalog
{
    // ponytail: catalog lives in code; move it to a table once partners need to edit it without a deploy
    private static readonly FrozenDictionary<string, Reward> Rewards = new Reward[]
    {
        new("cinema-ticket", "Cinema ticket", Credits.Of(10)),
        new("perfume-kit", "Perfume kit", Credits.Of(20)),
        new("thermal-bottle", "Thermal bottle", Credits.Of(40)),
    }.ToFrozenDictionary(reward => reward.Id);

    public static Result<Reward> Find(string id) =>
        Rewards.TryGetValue(id, out var reward) ? reward : MembershipErrors.RewardNotFound;
}
