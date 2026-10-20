using Clube.Application.Memberships;
using Clube.Domain.Memberships;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Clube.Api.Memberships;

public sealed record SubscribeRequest(string Cpf);

public sealed record CreditAllowanceRequest(string Period);

public sealed record RedeemRequest(string RewardId);

public static class MembershipEndpoints
{
    public static void MapMemberships(this IEndpointRouteBuilder app)
    {
        var memberships = app.MapGroup("/memberships").WithTags("Memberships");

        memberships.MapPost("/", Subscribe);
        memberships.MapGet("/{id:guid}", Get);
        memberships.MapPost("/{id:guid}/allowance-credits", CreditAllowance);
        memberships.MapPost("/{id:guid}/redemptions", Redeem);
    }

    private static async Task<Results<Created<MembershipView>, ProblemHttpResult>> Subscribe(
        SubscribeRequest request, SubscribeMember subscribe, CancellationToken ct)
    {
        var result = await subscribe.ExecuteAsync(request.Cpf, ct);
        return result.TryGetValue(out var view, out var error)
            ? TypedResults.Created($"/memberships/{view.Id}", view)
            : error.ToProblem();
    }

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> Get(
        Guid id, GetMembership getMembership, CancellationToken ct)
    {
        var result = await getMembership.ExecuteAsync(new MembershipId(id), ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> CreditAllowance(
        Guid id, CreditAllowanceRequest request, CreditMonthlyAllowance credit, CancellationToken ct)
    {
        var result = await credit.ExecuteAsync(new MembershipId(id), request.Period, ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> Redeem(
        Guid id, RedeemRequest request, RedeemReward redeem, CancellationToken ct)
    {
        var result = await redeem.ExecuteAsync(new MembershipId(id), request.RewardId, ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }
}
