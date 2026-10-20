using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;

namespace Clube.Core.Ports.Driving;

public sealed record MembershipView(Guid Id, string Holder, long BalanceCents, DateTimeOffset SubscribedAt)
{
    public static MembershipView From(Membership membership, Money balance) => new(
        membership.Id.Value,
        membership.Holder.Masked,
        balance.Cents,
        membership.SubscribedAt);
}

public sealed record StatementView(Guid MembershipId, long BalanceCents, IReadOnlyList<StatementLine> Lines, StatementCursor? Next);
