using Clube.Domain.Shared;

namespace Clube.Domain.Tests;

internal static class ResultAssert
{
    public static T Ok<T>(Result<T> result) where T : notnull
    {
        Assert.True(result.TryGetValue(out var value, out var error), $"Expected success, got {error?.Code}");
        return value;
    }

    public static Error Failed<T>(Result<T> result) where T : notnull
    {
        Assert.False(result.TryGetValue(out _, out var error), "Expected failure, got success");
        return error;
    }
}
