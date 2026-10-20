using Clube.Domain.Shared;

namespace Clube.Domain.Memberships;

public sealed record Cpf
{
    private const int Length = 11;

    public string Digits { get; }

    private Cpf(string digits) => Digits = digits;

    public string Masked => $"***.{Digits[3..6]}.{Digits[6..9]}-**";

    public static Result<Cpf> Parse(string raw)
    {
        var digits = raw.Replace(".", "").Replace("-", "");
        if (!IsWellFormed(digits)) return MembershipErrors.InvalidCpf;
        if (!HasValidCheckDigits(digits)) return MembershipErrors.InvalidCpf;

        return new Cpf(digits);
    }

    // Keeps PII out of logs and string interpolation by default.
    public override string ToString() => Masked;

    // Repeated digits (111.111.111-11) satisfy the checksum but are never issued.
    private static bool IsWellFormed(string digits) =>
        digits.Length == Length && digits.All(char.IsAsciiDigit) && digits.Distinct().Count() > 1;

    private static bool HasValidCheckDigits(string digits) =>
        CheckDigit(digits[..9]) == digits[9] - '0' && CheckDigit(digits[..10]) == digits[10] - '0';

    private static int CheckDigit(string prefix)
    {
        var firstWeight = prefix.Length + 1;
        var sum = prefix.Select((digit, index) => (digit - '0') * (firstWeight - index)).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
