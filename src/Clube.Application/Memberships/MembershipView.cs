using Clube.Domain.Memberships;

namespace Clube.Application.Memberships;

public sealed record MembershipView(
    Guid Id,
    string Holder,
    int Balance,
    string? LastCreditedPeriod,
    DateTimeOffset SubscribedAt)
{
    public static MembershipView From(Membership membership) => new(
        membership.Id.Value,
        membership.Holder.Masked,
        membership.Balance.Value,
        membership.LastCreditedPeriod?.ToString(),
        membership.SubscribedAt);
}
