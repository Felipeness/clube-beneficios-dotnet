using System.Collections.Frozen;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public sealed record Reward(string Id, string Name, Money ListPrice, decimal PromotionalDiscount = 0m)
{
    public Money Price => ListPrice.Subtract(ListPrice.PercentOf(PromotionalDiscount));
}

public static class RewardCatalog
{
    // ponytail: catalog lives in code; move it to a table once partners need to edit it without a deploy
    private static readonly FrozenDictionary<string, Reward> Rewards = new Reward[]
    {
        new("cinema-ticket", "Cinema ticket", Money.FromCents(1000)),
        new("perfume-kit", "Perfume kit", Money.FromCents(2000)),
        // 15% of R$ 29,90 is R$ 4,485: commercial rounding gives R$ 4,49 off, banker's rounding would give R$ 4,48.
        new("thermal-bottle", "Thermal bottle", Money.FromCents(2990), PromotionalDiscount: 0.15m),
    }.ToFrozenDictionary(reward => reward.Id);

    public static Result<Reward> Find(string id) =>
        Rewards.TryGetValue(id, out Reward? reward) ? reward : MembershipErrors.RewardNotFound;
}
