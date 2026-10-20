using Clube.Core.Domain.Memberships;

namespace Clube.Core.Domain.Ledger;

// New callers (webhook, internal jobs) are one more member here plus one factory below.
public enum ActorKind
{
    Anonymous,
    Member,
    PaymentProvider,
    Operator,
}

// Who triggered a posting. Built only through the factories, so "anonymous with an id" cannot exist.
// The id is a stable identifier of the caller (never a CPF or any other PII).
public sealed record Actor
{
    public static readonly Actor Anonymous = new(ActorKind.Anonymous, null);

    public static readonly Actor PixProvider = new(ActorKind.PaymentProvider, "pix");

    public ActorKind Kind { get; }
    public string? Id { get; }

    private Actor(ActorKind kind, string? id)
    {
        Kind = kind;
        Id = id;
    }

    // Only staff and the payment provider (refunds, MED) may undo a posting; a member reversing a redemption would mint credit.
    public bool MayReverseTransactions => Kind is ActorKind.Operator or ActorKind.PaymentProvider;

    public static Actor Member(MembershipId id) => new(ActorKind.Member, id.ToString());

    // The subject is the identity provider's stable user id, so an audit leads back to one person.
    public static Actor Operator(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return new(ActorKind.Operator, subject);
    }
}
