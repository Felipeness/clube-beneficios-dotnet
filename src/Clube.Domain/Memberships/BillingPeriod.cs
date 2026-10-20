using System.Globalization;
using Clube.Domain.Shared;

namespace Clube.Domain.Memberships;

public readonly record struct BillingPeriod : IComparable<BillingPeriod>
{
    private const string Format = "yyyy-MM";

    public DateOnly Start { get; }

    private BillingPeriod(DateOnly start) => Start = start;

    public static BillingPeriod Containing(DateOnly date) => new(new DateOnly(date.Year, date.Month, 1));

    public static Result<BillingPeriod> Parse(string raw) =>
        DateOnly.TryParseExact(raw, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? Containing(start)
            : MembershipErrors.InvalidPeriod;

    public int CompareTo(BillingPeriod other) => Start.CompareTo(other.Start);

    public static bool operator <(BillingPeriod left, BillingPeriod right) => left.CompareTo(right) < 0;

    public static bool operator >(BillingPeriod left, BillingPeriod right) => left.CompareTo(right) > 0;

    public override string ToString() => Start.ToString(Format, CultureInfo.InvariantCulture);
}
