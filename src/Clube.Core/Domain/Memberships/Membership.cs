using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public readonly record struct MembershipId(Guid Value)
{
    public static MembershipId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public sealed class Membership
{
    public static readonly Credits MonthlyAllowance = Credits.Of(20);

    public MembershipId Id { get; private set; }
    public Cpf Holder { get; private set; }
    public DateTimeOffset SubscribedAt { get; private set; }
    public Credits Balance { get; private set; }
    public BillingPeriod? LastCreditedPeriod { get; private set; }

    // Parameter names match the properties so EF Core binds through this constructor.
    private Membership(MembershipId id, Cpf holder, DateTimeOffset subscribedAt)
    {
        Id = id;
        Holder = holder;
        SubscribedAt = subscribedAt;
        Balance = Credits.Zero;
    }

    public static Membership Subscribe(Cpf holder, DateTimeOffset now) => new(MembershipId.New(), holder, now);

    public Result<Credits> CreditMonthlyAllowance(BillingPeriod period)
    {
        // A retried charge for the same period must not credit twice.
        if (LastCreditedPeriod == period) return Balance;
        if (LastCreditedPeriod > period) return MembershipErrors.PeriodOutOfOrder;

        Balance = Balance.Add(MonthlyAllowance);
        LastCreditedPeriod = period;
        return Balance;
    }

    public Result<Credits> Redeem(Reward reward)
    {
        if (!Balance.Covers(reward.Cost)) return MembershipErrors.InsufficientCredits;

        Balance = Balance.Subtract(reward.Cost);
        return Balance;
    }
}
