using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Clube.Adapters.Http;

// The claims this API trusts, as the identity provider (or dotnet user-jwts) writes them. Inbound claim mapping is off,
// so these are the raw JWT names and nothing is renamed behind the reader's back.
internal static class ClubeClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string MembershipId = "membership_id";
    public const string OperatorRole = "operator";

    // Null unless the token carries the operator role and a subject: the subject is what the ledger records.
    public static string? OperatorSubjectOf(ClaimsPrincipal user) =>
        user.IsInRole(OperatorRole) && user.FindFirst(Subject)?.Value is { Length: > 0 } subject ? subject : null;

    public static Guid? MembershipIdOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(MembershipId)?.Value, out Guid id) ? id : null;
}

internal static class MembershipPolicies
{
    // The member it belongs to, or an operator (support looks at any account).
    public const string ReadMembership = "membership:read";
    // Only the member it belongs to: an operator does not spend a member's balance.
    public const string ActAsMember = "membership:act";
    public const string Operator = "operator";

    public static void AddMembershipPolicies(this AuthorizationBuilder authorization) => authorization
        .AddPolicy(ReadMembership, policy => policy.AddRequirements(MembershipAccessRequirement.MemberOrOperator))
        .AddPolicy(ActAsMember, policy => policy.AddRequirements(MembershipAccessRequirement.MemberOnly))
        // The subject is what the ledger records as the operator, so a token without one is not an operator.
        .AddPolicy(Operator, policy => policy.RequireRole(ClubeClaims.OperatorRole).RequireClaim(ClubeClaims.Subject));
}

internal sealed record MembershipAccessRequirement(bool OperatorsAllowed) : IAuthorizationRequirement
{
    public static readonly MembershipAccessRequirement MemberOrOperator = new(OperatorsAllowed: true);
    public static readonly MembershipAccessRequirement MemberOnly = new(OperatorsAllowed: false);
}

// Resource-based: the resource is the membership named by the route's {id}, compared with the token before any lookup.
// Failing here is a 403 for every id that is not the caller's, existing or not, so the answer reveals nothing.
internal sealed class MembershipAccessHandler : AuthorizationHandler<MembershipAccessRequirement, HttpContext>
{
    private const string RouteKey = "id";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MembershipAccessRequirement requirement, HttpContext resource)
    {
        if (IsAllowed(context.User, requirement, resource)) context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private static bool IsAllowed(ClaimsPrincipal user, MembershipAccessRequirement requirement, HttpContext request)
    {
        if (requirement.OperatorsAllowed && ClubeClaims.OperatorSubjectOf(user) is not null) return true;

        Guid? owned = ClubeClaims.MembershipIdOf(user);
        return owned is not null && Guid.TryParse(request.Request.RouteValues[RouteKey]?.ToString(), out Guid requested) && requested == owned;
    }
}
