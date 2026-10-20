using System.Diagnostics;
using Clube.Core.Domain.Ledger;
using Clube.Core.Ports.Driven;
using Microsoft.AspNetCore.Http;

namespace Clube.Adapters.Http;

// Driven adapter: answers "who is calling" from the current HTTP request.
// A request is anonymous unless an endpoint proved who sent it (the Pix webhook, by its signature);
// the trace id honours an incoming traceparent header.
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
            Actor actor = request.Items.TryGetValue(ActorKey, out object? proven) && proven is Actor known ? known : Actor.Anonymous;
            return new CallerContext(actor, correlationId);
        }
    }

    // Called only after the request's authenticity was verified, so the actor is a fact, not a claim.
    public static void Authenticate(HttpContext request, Actor actor) => request.Items[ActorKey] = actor;
}
