using System.Globalization;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public readonly record struct BillingPeriod
{
    private const string Format = "yyyy-MM";

    // Billing months follow the club's calendar, not UTC: 21:30 on Oct 31 in Sao Paulo is already Nov 1 in UTC.
    private static readonly TimeZoneInfo BillingZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public DateOnly Start { get; }

    private BillingPeriod(DateOnly start) => Start = start;

    public static BillingPeriod Containing(DateOnly date) => new(new DateOnly(date.Year, date.Month, 1));

    public static BillingPeriod At(DateTimeOffset instant) =>
        Containing(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, BillingZone).DateTime));

    public static Result<BillingPeriod> Parse(string raw) =>
        DateOnly.TryParseExact(raw, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? Containing(start)
            : MembershipErrors.InvalidPeriod;

    public override string ToString() => Start.ToString(Format, CultureInfo.InvariantCulture);
}
