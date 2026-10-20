using System.Globalization;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public readonly record struct BillingPeriod
{
    private const string Format = "yyyy-MM";

    public DateOnly Start { get; }

    private BillingPeriod(DateOnly start) => Start = start;

    public static BillingPeriod Containing(DateOnly date) => new(new DateOnly(date.Year, date.Month, 1));

    public static Result<BillingPeriod> Parse(string raw) =>
        DateOnly.TryParseExact(raw, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? Containing(start)
            : MembershipErrors.InvalidPeriod;

    public override string ToString() => Start.ToString(Format, CultureInfo.InvariantCulture);
}
