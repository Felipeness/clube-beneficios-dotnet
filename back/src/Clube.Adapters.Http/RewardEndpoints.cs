using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Clube.Adapters.Http;

// Driving adapter for the catalog. Public on purpose: it names no member, and a visitor deciding whether to join
// needs to see what the club offers. Prices come from the hexagon, so the discount rule lives in one place.
internal static class RewardEndpoints
{
    public static void MapRewards(this IEndpointRouteBuilder app) =>
        app.MapGet("/rewards", (IForManagingMemberships memberships) => TypedResults.Ok(memberships.ListRewards()))
            .WithTags("Rewards")
            .WithSummary("Rewards a member can redeem, with list price, discount and final price in cents")
            .Produces<IReadOnlyList<RewardView>>(StatusCodes.Status200OK);
}
