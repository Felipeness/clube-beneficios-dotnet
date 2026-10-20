using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Clube.Adapters.Http;

public static class PixWebhookEndpoints
{
    // A real event is a few hundred bytes; anything far larger is refused before it is buffered for the signature check.
    private const long MaxBodyBytes = 16 * 1024;

    // The development tools find this endpoint by name to deliver simulated events through it, signature check included.
    public const string EndpointName = "ReceivePixWebhook";

    internal static void MapPixWebhooks(this IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/pix", ([AsParameters] PixDelivery delivery, PixWebhookReceiver receiver, CancellationToken ct) => receiver.ReceiveAsync(delivery, ct))
            .WithName(EndpointName)
            .WithTags("Webhooks")
            .WithSummary("Pix payment provider events (pix.received, pix.refunded), signed with HMAC-SHA256")
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .Accepts<PixWebhookEvent>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
