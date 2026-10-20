using System.Diagnostics;
using Clube.Core.Domain.Ledger;
using Clube.Core.Ports.Driven;
using Microsoft.AspNetCore.Http;

namespace Clube.Adapters.Http;

// Driven adapter: answers "who is calling" from the current HTTP request.
// Every call is anonymous until authentication exists; the trace id honours an incoming traceparent header.
internal sealed class HttpCaller(IHttpContextAccessor accessor) : IForIdentifyingTheCaller
{
    public CallerContext Current
    {
        get
        {
            HttpContext request = accessor.HttpContext
                ?? throw new InvalidOperationException("No HTTP request in scope to identify the caller.");
            string correlationId = Activity.Current?.TraceId.ToString() ?? request.TraceIdentifier;
            return new CallerContext(Actor.Anonymous, correlationId);
        }
    }
}
