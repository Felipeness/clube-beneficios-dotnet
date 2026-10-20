using System.Diagnostics;
using System.Security.Claims;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driven;
using Microsoft.AspNetCore.Http;

namespace Clube.Adapters.Http;

// Driven adapter: answers "who is calling" from the current HTTP request.
// The actor is only ever a proven fact: an endpoint that verified a signature (the Pix webhook), or a validated JWT
// (operator, then member). Headers and bodies never name the actor. The trace id honours an incoming traceparent header.
internal sealed class HttpCaller(IHttpContextAccessor accessor) : IForIdentifyingTheCaller
{
    private static readonly object ActorKey = new();

    public CallerContext Current
    {
        get
        {
            HttpContext request = accessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP request in scope to identify the caller.");
            string correlationId = Activity.Current?.TraceId.ToString() ?? request.TraceIdentifier;
            return new CallerContext(ActorOf(request), correlationId);
        }
    }

    private static Actor ActorOf(HttpContext request)
    {
        if (request.Items.TryGetValue(ActorKey, out object? proven) && proven is Actor known) return known;

        return ActorOf(request.User);
    }

    // The JWT bearer handler only fills User from a token whose signature, issuer, audience and lifetime checked out.
    private static Actor ActorOf(ClaimsPrincipal user)
    {
        if (ClubeClaims.OperatorSubjectOf(user) is string subject) return Actor.Operator(subject);

        Guid? member = ClubeClaims.MembershipIdOf(user);
        return member is Guid id ? Actor.Member(new MembershipId(id)) : Actor.Anonymous;
    }

    // Called only after the request's authenticity was verified, so the actor is a fact, not a claim.
    public static void Authenticate(HttpContext request, Actor actor) => request.Items[ActorKey] = actor;
}
