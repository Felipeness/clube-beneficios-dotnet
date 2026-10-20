using System.ComponentModel;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Clube.Adapters.Http;

public sealed record SubscribeRequest(
    [property: Description("11 digits, with or without punctuation (529.982.247-25). Check digits are validated; repeated digits are rejected.")]
    string Cpf);

public sealed record CreditAllowanceRequest(
    [property: Description("Billing period as yyyy-MM. Credits R$ 20,00 once per period; re-sending it is a no-op. Must fall between the subscription month and the current month (America/Sao_Paulo), otherwise 422.")]
    string Period);

public sealed record RedeemRequest(
    [property: Description("One of: cinema-ticket (R$ 10,00), perfume-kit (R$ 20,00), thermal-bottle (R$ 29,90 with 15% off: R$ 25,41).")]
    string RewardId);

public sealed record StatementResponse(
    Guid MembershipId,
    long BalanceCents,
    IReadOnlyList<StatementLine> Lines,
    [property: Description("Pass as ?cursor= to get the next (older) page; null on the last page. Opaque: do not parse it.")]
    string? NextCursor);

// Driving adapter: translates HTTP into calls on the driving port and Results back into HTTP.
// It knows nothing about storage; it only sees IForManagingMemberships.
internal static class MembershipEndpoints
{
    public static void MapMemberships(this IEndpointRouteBuilder app)
    {
        var memberships = app.MapGroup("/memberships").WithTags("Memberships");

        // ProblemHttpResult carries no status metadata, so error responses must be declared
        // explicitly or the OpenAPI document advertises only the success path.
        memberships.MapPost("/", Subscribe)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        memberships.MapGet("/{id:guid}", Get)
            .ProducesProblem(StatusCodes.Status404NotFound);
        memberships.MapGet("/{id:guid}/statement", GetStatement)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        memberships.MapPost("/{id:guid}/allowance-credits", CreditAllowance)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        memberships.MapPost("/{id:guid}/redemptions", Redeem)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        // Anonymous for now: once callers authenticate, only operators may reverse a posting.
        memberships.MapPost("/{id:guid}/transactions/{transactionId:guid}/reversal", Reverse)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Created<MembershipView>, ProblemHttpResult>> Subscribe(
        SubscribeRequest request, IForManagingMemberships memberships, CancellationToken ct)
    {
        var result = await memberships.SubscribeAsync(request.Cpf, ct);
        return result.TryGetValue(out var view, out var error)
            ? TypedResults.Created($"/memberships/{view.Id}", view)
            : error.ToProblem();
    }

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> Get(
        Guid id, IForManagingMemberships memberships, CancellationToken ct)
    {
        var result = await memberships.GetAsync(new MembershipId(id), ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }

    private static async Task<Results<Ok<StatementResponse>, ProblemHttpResult>> GetStatement(
        Guid id,
        [Description("Page size, 1 to 100 (default 50). Out of range is a 400, never silently clamped.")] int? limit,
        [Description("The nextCursor of the previous page. Omit for the newest entries.")] string? cursor,
        IForManagingMemberships memberships,
        CancellationToken ct)
    {
        if (!ParsePage(limit, cursor).TryGetValue(out var page, out var invalidPage)) return invalidPage.ToProblem();

        var result = await memberships.GetStatementAsync(new MembershipId(id), page, ct);
        return result.TryGetValue(out var statement, out var error) ? TypedResults.Ok(ToResponse(statement)) : error.ToProblem();
    }

    private static Result<StatementPage> ParsePage(int? limit, string? cursor)
    {
        if (cursor is null) return StatementPage.Create(limit, null);
        if (!StatementCursorCodec.Decode(cursor).TryGetValue(out var after, out var invalidCursor)) return invalidCursor;

        return StatementPage.Create(limit, after);
    }

    private static StatementResponse ToResponse(StatementView statement) => new(
        statement.MembershipId,
        statement.BalanceCents,
        statement.Lines,
        statement.Next is null ? null : StatementCursorCodec.Encode(statement.Next));

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> CreditAllowance(
        Guid id, CreditAllowanceRequest request, IForManagingMemberships memberships, CancellationToken ct)
    {
        var result = await memberships.CreditMonthlyAllowanceAsync(new MembershipId(id), request.Period, ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> Redeem(
        Guid id,
        RedeemRequest request,
        // Required: a missing header is a 400, so a client cannot redeem without making retries safe.
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        IForManagingMemberships memberships,
        CancellationToken ct)
    {
        var result = await memberships.RedeemRewardAsync(new MembershipId(id), request.RewardId, idempotencyKey, ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> Reverse(
        Guid id, Guid transactionId, IForManagingMemberships memberships, CancellationToken ct)
    {
        var result = await memberships.ReverseTransactionAsync(new MembershipId(id), transactionId, ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }
}
