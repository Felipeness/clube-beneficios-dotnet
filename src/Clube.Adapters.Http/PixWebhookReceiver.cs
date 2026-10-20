using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Clube.Adapters.Http;

internal sealed record PixDelivery(
    HttpRequest Request,
    [property: Description("Unix seconds when the provider signed the delivery; more than 5 minutes off is a 401.")]
    [FromHeader(Name = "X-Pix-Timestamp")] string? Timestamp,
    [property: Description("Hex HMAC-SHA256 of \"{timestamp}.{raw body}\" with the shared webhook secret.")]
    [FromHeader(Name = "X-Pix-Signature")] string? Signature);

// Driving adapter for the payment provider: checks the delivery is authentic, then translates each event into one
// call on the driving port. Which outcome means what for the ledger is decided by the hexagon, not here.
internal sealed partial class PixWebhookReceiver(IForManagingMemberships memberships, PixSignature signature, ILogger<PixWebhookReceiver> logger)
{
    // The provider's format, not this API's: field order is not guaranteed, so "type" may come anywhere.
    private static readonly JsonSerializerOptions WireFormat = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public async Task<Results<Ok<PixWebhookResponse>, ProblemHttpResult>> ReceiveAsync(PixDelivery delivery, CancellationToken ct)
    {
        // Read raw: the signature covers the exact bytes sent, which a model binder would already have reinterpreted.
        byte[] body = await ReadBodyAsync(delivery.Request, ct);
        if (!signature.IsAuthentic(delivery.Timestamp, delivery.Signature, body))
            return TypedResults.Problem(title: "Webhook signature is missing, invalid or expired.", statusCode: StatusCodes.Status401Unauthorized);

        HttpCaller.Authenticate(delivery.Request.HttpContext, Actor.PixProvider);
        return ParseEvent(body) switch
        {
            PixReceived received => await CreditAsync(received, ct),
            PixRefunded refunded => await RefundAsync(refunded, ct),
            null => TypedResults.Problem(title: "Webhook payload is not a supported Pix event.", statusCode: StatusCodes.Status400BadRequest),
            var other => throw new UnreachableException($"Unmapped Pix event {other.GetType().Name}."),
        };
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private PixWebhookEvent? ParseEvent(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<PixWebhookEvent>(body, WireFormat);
        }
        // Malformed JSON, a missing field or an event type this contract does not know (NotSupported when "type" is absent).
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            LogUnreadablePayload(exception);
            return null;
        }
    }

    private async Task<Results<Ok<PixWebhookResponse>, ProblemHttpResult>> CreditAsync(PixReceived received, CancellationToken ct)
    {
        var payment = new ReceivedPayment(received.EndToEndId, received.Period, received.AmountCents);
        var result = await memberships.CreditMonthlyAllowanceAsync(new MembershipId(received.MembershipId), payment, ct);
        if (!result.TryGetValue(out var outcome, out var error)) return Rejected(PixReceived.Type, received.EndToEndId, error);

        if (outcome == AllowanceCreditOutcome.DuplicatePayment) LogDuplicatePayment(received.EndToEndId, received.MembershipId, received.Period);
        return Answer(outcome);
    }

    private async Task<Results<Ok<PixWebhookResponse>, ProblemHttpResult>> RefundAsync(PixRefunded refunded, CancellationToken ct)
    {
        var result = await memberships.RefundPaymentAsync(refunded.OriginalEndToEndId, ct);
        if (!result.TryGetValue(out var outcome, out var error)) return Rejected(PixRefunded.Type, refunded.EndToEndId, error);

        if (outcome == PaymentRefundOutcome.UnknownPayment) LogUnknownRefund(refunded.EndToEndId, refunded.OriginalEndToEndId);
        return Answer(outcome);
    }

    private ProblemHttpResult Rejected(string eventType, string endToEndId, Error error)
    {
        LogRejected(eventType, endToEndId, error.Code);
        return error.ToProblem();
    }

    private static Ok<PixWebhookResponse> Answer(Enum outcome) =>
        TypedResults.Ok(new PixWebhookResponse(JsonNamingPolicy.SnakeCaseLower.ConvertName(outcome.ToString())));

    // Money arrived and nothing was credited for it: support has to send this Pix back. No CPF, only stable ids.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Duplicate Pix payment {EndToEndId} for membership {MembershipId} period {Period}: already credited, refund it")]
    private partial void LogDuplicatePayment(string endToEndId, Guid membershipId, string period);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pix refund {EndToEndId} points at unknown payment {OriginalEndToEndId}: nothing reversed")]
    private partial void LogUnknownRefund(string endToEndId, string originalEndToEndId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pix event {EventType} {EndToEndId} rejected with {ErrorCode}")]
    private partial void LogRejected(string eventType, string endToEndId, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authentic Pix webhook with an unreadable payload")]
    private partial void LogUnreadablePayload(Exception exception);
}
