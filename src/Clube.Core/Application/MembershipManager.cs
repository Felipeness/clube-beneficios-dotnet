using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driven;
using Clube.Core.Ports.Driving;

namespace Clube.Core.Application;

// Implements the driving port by orchestrating the domain and calling out through driven ports.
// TimeProvider is a driven port too; the BCL already ships it as an abstraction with a fake for tests.
public sealed class MembershipManager(IForStoringMemberships memberships, TimeProvider clock) : IForManagingMemberships
{
    public async Task<Result<MembershipView>> SubscribeAsync(string cpf, CancellationToken ct)
    {
        if (!Cpf.Parse(cpf).TryGetValue(out var holder, out var invalidCpf)) return invalidCpf;

        var membership = Membership.Subscribe(holder, clock.GetUtcNow());
        memberships.Add(membership);

        // The store's uniqueness rule is the source of truth; a pre-check would still race.
        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.AlreadySubscribed;
        return MembershipView.From(membership);
    }

    public async Task<Result<MembershipView>> GetAsync(MembershipId id, CancellationToken ct)
    {
        var membership = await memberships.FindAsync(id, ct);
        return membership is null ? MembershipErrors.NotFound : MembershipView.From(membership);
    }

    public async Task<Result<MembershipView>> CreditMonthlyAllowanceAsync(MembershipId id, string period, CancellationToken ct)
    {
        if (!BillingPeriod.Parse(period).TryGetValue(out var billingPeriod, out var invalidPeriod)) return invalidPeriod;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        if (!membership.CreditMonthlyAllowance(billingPeriod).TryGetValue(out _, out var rejected)) return rejected;

        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.ConcurrentUpdate;
        return MembershipView.From(membership);
    }

    public async Task<Result<MembershipView>> RedeemRewardAsync(MembershipId id, string rewardId, CancellationToken ct)
    {
        if (!RewardCatalog.Find(rewardId).TryGetValue(out var reward, out var unknownReward)) return unknownReward;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        if (!membership.Redeem(reward).TryGetValue(out _, out var rejected)) return rejected;

        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.ConcurrentUpdate;
        return MembershipView.From(membership);
    }
}
