using System.ComponentModel;
using Clube.Core.Domain.Reconciliation;
using Clube.Core.Ports.Driving;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Clube.Adapters.Http;

public sealed record DivergenceResponse(DivergenceKind Kind, string EndToEndId, long? PspCents, long? LedgerCents);

public sealed record ReconciliationResponse(string Day, int SettledAtPsp, int CreditedInLedger, IReadOnlyList<DivergenceResponse> Divergences)
{
    public static ReconciliationResponse From(ReconciliationReport report) => new(
        report.Day.ToString("yyyy-MM-dd"),
        report.SettledAtPsp,
        report.CreditedInLedger,
        [.. report.Divergences.Select(divergence => new DivergenceResponse(divergence.Kind, divergence.EndToEndId.Value, divergence.PspAmount?.Cents, divergence.LedgerAmount?.Cents))]);
}

// Driving adapter for the operator: the same report the daily worker logs, on demand for any day.
internal static class ReconciliationEndpoints
{
    public static void MapReconciliation(this IEndpointRouteBuilder app) =>
        app.MapGet("/operations/reconciliation", Reconcile)
            .WithTags("Operations")
            .RequireAuthorization(MembershipPolicies.Operator)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<Ok<ReconciliationResponse>> Reconcile(
        [Description("The Sao Paulo calendar day to reconcile, as yyyy-MM-dd.")] DateOnly date,
        IForReconcilingWithThePsp reconciliation,
        CancellationToken ct) => TypedResults.Ok(ReconciliationResponse.From(await reconciliation.RunAsync(date, ct)));
}
