using System.Diagnostics;
using Clube.Core.Domain.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Clube.Adapters.Http;

internal static class ErrorResults
{
    public static ProblemHttpResult ToProblem(this Error error) => TypedResults.Problem(
        title: error.Message,
        statusCode: StatusCodeFor(error.Kind),
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    private static int StatusCodeFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        _ => throw new UnreachableException($"Unmapped error kind {kind}."),
    };
}
