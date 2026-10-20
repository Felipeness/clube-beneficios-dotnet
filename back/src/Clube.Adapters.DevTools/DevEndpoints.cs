using System.ComponentModel;
using Clube.Adapters.Http;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Vouchers;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Clube.Adapters.DevTools;

public sealed record TokenRequest(
    [property: Description("member or operator.")] string Role,
    [property: Description("Required for member: the membership the token belongs to.")] Guid? MembershipId = null,
    [property: Description("Required for operator: the name the ledger records as the actor.")] string? Name = null);

public sealed record PixPaymentRequest(Guid MembershipId, [property: Description("Billing period paid, as yyyy-MM.")] string Period);

public sealed record PixRefundRequest(string OriginalEndToEndId);

public sealed record SimulatedPixResponse(
    string EndToEndId,
    [property: Description("The webhook's own answer: credited, already_credited, duplicate_payment, refunded, already_refunded or unknown_payment.")]
    string Result);

public sealed record OutboxRunResponse(int Attempted, int Delivered, int Rescheduled, int DeadLettered);

public sealed record CreditExpiryFailureResponse(Guid MembershipId, string Error);

public sealed record CreditExpiryRunResponse(string Cutoff, int Expired, long ExpiredCents, IReadOnlyList<CreditExpiryFailureResponse> Failures);

public sealed record ReconciliationJobRequest(DateOnly Date);

public sealed record ClockRequest(
    [property: Description("Days to move the dev clock forward, 1 to 3650 in total. Omit when resetting.")] int? AdvanceDays = null,
    [property: Description("true puts the dev clock back on the real time.")] bool? Reset = null);

public sealed record ClockResponse(DateTimeOffset Now, int OffsetDays);

// Driving adapter for local development only (see DevToolsAdapter): lets the app and a demo drive what would otherwise need
// a payment provider, an identity provider, a scheduler and a calendar. Nothing here is reachable outside Development.
internal static class DevEndpoints
{
    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev").WithTags("Dev").AllowAnonymous();

        dev.MapPost("/tokens", IssueToken).ProducesProblem(StatusCodes.Status400BadRequest);
        dev.MapPost("/pix-payments", PayPix).ProducesProblem(StatusCodes.Status400BadRequest);
        dev.MapPost("/pix-refunds", RefundPix).ProducesProblem(StatusCodes.Status400BadRequest);
        dev.MapPost("/jobs/outbox", RunOutbox);
        dev.MapPost("/jobs/credit-expiry", RunCreditExpiry);
        dev.MapPost("/jobs/reconciliation", RunReconciliation);
        dev.MapGet("/clock", ReadClock);
        dev.MapPost("/clock", MoveClock).ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static IResult IssueToken(TokenRequest request, DevTokenIssuer issuer)
    {
        if (ClaimsFor(request) is not { } claims)
            return Problem("A member token needs role=member and membershipId; an operator token needs role=operator and name.");

        return issuer.Issue(claims) is { } token
            ? TypedResults.Ok(token)
            : Problem("No symmetric signing key is configured for Bearer. Run: dotnet user-jwts create (in src/Clube.Host).", StatusCodes.Status500InternalServerError);
    }

    private static Dictionary<string, object>? ClaimsFor(TokenRequest request) => request switch
    {
        { Role: "member", MembershipId: { } membershipId } => DevTokenIssuer.MemberClaims(membershipId),
        { Role: "operator", Name: { Length: > 0 } name } => DevTokenIssuer.OperatorClaims(name),
        _ => null,
    };

    private static async Task<IResult> PayPix(PixPaymentRequest request, PixSimulator pix, CancellationToken ct) =>
        Answer(await pix.PayAsync(request.MembershipId, request.Period, ct));

    private static async Task<IResult> RefundPix(PixRefundRequest request, PixSimulator pix, CancellationToken ct) =>
        Answer(await pix.RefundAsync(request.OriginalEndToEndId, ct));

    // Relays the webhook's refusal untouched (status and ProblemDetails), so the app shows what the API really said.
    private static IResult Answer(SimulatedPix simulated) => simulated.Result is { } result
        ? TypedResults.Ok(new SimulatedPixResponse(simulated.EndToEndId, result))
        : TypedResults.Content(simulated.Answer.Body, "application/problem+json", statusCode: simulated.Answer.StatusCode);

    private static async Task<IResult> RunOutbox(IForRelayingTheOutbox outbox, CancellationToken ct)
    {
        var settled = await outbox.DispatchBatchAsync(ct);
        return TypedResults.Ok(new OutboxRunResponse(
            settled.Count,
            settled.Count(message => message.Status == OutboxStatus.Delivered),
            settled.Count(message => message.Status == OutboxStatus.Pending),
            settled.Count(message => message.Status == OutboxStatus.DeadLettered)));
    }

    private static async Task<IResult> RunCreditExpiry(IForExpiringCredits expiry, TimeProvider clock, CancellationToken ct)
    {
        var report = await expiry.RunAsync(CreditExpiry.CutoffAt(clock.GetUtcNow()), ct);
        return TypedResults.Ok(new CreditExpiryRunResponse(
            report.Cutoff.ToString(),
            report.Expired,
            report.ExpiredCents,
            [.. report.Failures.Select(failure => new CreditExpiryFailureResponse(failure.Member.Value, failure.Error))]));
    }

    private static async Task<IResult> RunReconciliation(ReconciliationJobRequest request, IForReconcilingWithThePsp reconciliation, CancellationToken ct) =>
        TypedResults.Ok(ReconciliationResponse.From(await reconciliation.RunAsync(request.Date, ct)));

    private static IResult ReadClock(DevClock clock) => TypedResults.Ok(ClockOf(clock));

    private static IResult MoveClock(ClockRequest request, DevClock clock)
    {
        switch (request)
        {
            case { Reset: true, AdvanceDays: null }:
                clock.Reset();
                break;
            case { AdvanceDays: { } days, Reset: null or false } when clock.TryAdvance(days):
                break;
            default:
                return Problem($"Send either advanceDays (1 to {DevClock.MaxOffsetDays}, {DevClock.MaxOffsetDays} days in total) or reset=true.");
        }

        return TypedResults.Ok(ClockOf(clock));
    }

    private static ClockResponse ClockOf(DevClock clock) => new(clock.GetUtcNow(), clock.OffsetDays);

    private static IResult Problem(string title, int status = StatusCodes.Status400BadRequest) => TypedResults.Problem(title: title, statusCode: status);
}
