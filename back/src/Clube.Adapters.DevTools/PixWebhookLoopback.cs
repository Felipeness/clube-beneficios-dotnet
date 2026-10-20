using System.Text;
using Clube.Adapters.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Clube.Adapters.DevTools;

internal sealed record SignedDelivery(string Body, string Timestamp, string Signature);

internal sealed record LoopbackAnswer(int StatusCode, string Body);

// Hands a signed delivery to the real /webhooks/pix endpoint, in memory: the same route, the same binding, the same
// receiver, the same signature check. Nothing is verified here, so a simulated payment is accepted or refused by the very
// code that guards the real one, and there is no second copy of the rules to drift.
internal sealed class PixWebhookLoopback(IEnumerable<EndpointDataSource> endpointSources, IServiceScopeFactory scopes, IHttpContextAccessor accessor)
{
    private const string Path = "/webhooks/pix";

    public async Task<LoopbackAnswer> DeliverAsync(SignedDelivery delivery, CancellationToken ct)
    {
        Endpoint endpoint = WebhookEndpoint();
        await using var scope = scopes.CreateAsyncScope();
        using var responseBody = new MemoryStream();
        DefaultHttpContext request = BuildRequest(delivery, scope.ServiceProvider, responseBody, ct);
        request.SetEndpoint(endpoint);

        await RunAsTheCurrentRequestAsync(request, endpoint);
        return new LoopbackAnswer(request.Response.StatusCode, Encoding.UTF8.GetString(responseBody.ToArray()));
    }

    private Endpoint WebhookEndpoint() => endpointSources
        .SelectMany(source => source.Endpoints)
        .FirstOrDefault(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == PixWebhookEndpoints.EndpointName)
        ?? throw new InvalidOperationException($"Endpoint {PixWebhookEndpoints.EndpointName} is not mapped.");

    private static DefaultHttpContext BuildRequest(SignedDelivery delivery, IServiceProvider services, Stream responseBody, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(delivery.Body);
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = ct };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = Path;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Request.Headers["X-Pix-Timestamp"] = delivery.Timestamp;
        context.Request.Headers["X-Pix-Signature"] = delivery.Signature;
        context.Response.Body = responseBody;
        return context;
    }

    // The caller of the ledger is read from the ambient request (see HttpCaller), which must be the webhook's, not the
    // /dev one that is serving this call, or the posting would be attributed to the wrong actor.
    private async Task RunAsTheCurrentRequestAsync(HttpContext request, Endpoint endpoint)
    {
        RequestDelegate handler = endpoint.RequestDelegate ?? throw new InvalidOperationException($"Endpoint {endpoint.DisplayName} has no handler.");
        HttpContext? outer = accessor.HttpContext;
        accessor.HttpContext = request;
        try
        {
            await handler(request);
        }
        finally
        {
            accessor.HttpContext = outer;
        }
    }
}
