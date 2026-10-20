using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Clube.Domain.Shared;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    BusinessRule,
}

public sealed record Error(string Code, string Message, ErrorKind Kind);

// C# has no discriminated unions yet: a closed hierarchy (private constructor) plus the Try pattern
// gives callers flow-analysed access without null-forgiving operators.
public abstract class Result<T> where T : notnull
{
    private Result() { }

    public bool TryGetValue([NotNullWhen(true)] out T? value, [NotNullWhen(false)] out Error? error)
    {
        switch (this)
        {
            case Ok ok:
                value = ok.Value;
                error = null;
                return true;
            case Failure failure:
                value = default;
                error = failure.Error;
                return false;
            default:
                throw new UnreachableException();
        }
    }

    public static implicit operator Result<T>(T value) => new Ok(value);

    public static implicit operator Result<T>(Error error) => new Failure(error);

    private sealed class Ok(T value) : Result<T>
    {
        public T Value { get; } = value;
    }

    private sealed class Failure(Error error) : Result<T>
    {
        public Error Error { get; } = error;
    }
}
