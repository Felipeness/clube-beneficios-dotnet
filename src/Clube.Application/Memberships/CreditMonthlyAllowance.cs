using Clube.Domain.Memberships;
using Clube.Domain.Shared;

namespace Clube.Application.Memberships;

public sealed class CreditMonthlyAllowance(IMembershipRepository memberships)
{
    public async Task<Result<MembershipView>> ExecuteAsync(MembershipId id, string rawPeriod, CancellationToken ct)
    {
        if (!BillingPeriod.Parse(rawPeriod).TryGetValue(out var period, out var invalidPeriod)) return invalidPeriod;

        var membership = await memberships.FindAsync(id, ct);
        if (membership is null) return MembershipErrors.NotFound;
        if (!membership.CreditMonthlyAllowance(period).TryGetValue(out _, out var rejected)) return rejected;

        if (!await memberships.TrySaveChangesAsync(ct)) return MembershipErrors.ConcurrentUpdate;
        return MembershipView.From(membership);
    }
}
