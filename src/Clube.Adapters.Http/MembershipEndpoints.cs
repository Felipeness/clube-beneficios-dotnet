using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Clube.Adapters.Http;

public sealed record SubscribeRequest(string Cpf);

public sealed record CreditAllowanceRequest(string Period);

public sealed record RedeemRequest(string RewardId);

// Driving adapter: translates HTTP into calls on the driving port and Results back into HTTP.
// It knows nothing about storage; it only sees IForManagingMemberships.
internal static class MembershipEndpoints
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

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> CreditAllowance(
        Guid id, CreditAllowanceRequest request, IForManagingMemberships memberships, CancellationToken ct)
    {
        var result = await memberships.CreditMonthlyAllowanceAsync(new MembershipId(id), request.Period, ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }

    private static async Task<Results<Ok<MembershipView>, ProblemHttpResult>> Redeem(
        Guid id, RedeemRequest request, IForManagingMemberships memberships, CancellationToken ct)
    {
        var result = await memberships.RedeemRewardAsync(new MembershipId(id), request.RewardId, ct);
        return result.TryGetValue(out var view, out var error) ? TypedResults.Ok(view) : error.ToProblem();
    }
}
