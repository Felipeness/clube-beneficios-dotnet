using Clube.Core.Domain.Memberships;

namespace Clube.Core.Domain.Ledger;

// New callers (webhook, internal jobs) are one more member here plus one factory below.
public enum ActorKind
{
    Anonymous,
    Member,
}

// Who triggered a posting. Built only through the factories, so "anonymous with an id" cannot exist.
// The id is a stable identifier of the caller (never a CPF or any other PII).
public sealed record Actor
{
    public static readonly Actor Anonymous = new(ActorKind.Anonymous, null);

    public ActorKind Kind { get; }
    public string? Id { get; }

    private Actor(ActorKind kind, string? id)
    {
        Kind = kind;
        Id = id;
    }

    public static Actor Member(MembershipId id) => new(ActorKind.Member, id.ToString());
}
