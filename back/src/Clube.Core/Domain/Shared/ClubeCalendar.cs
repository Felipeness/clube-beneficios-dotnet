namespace Clube.Core.Domain.Shared;

// The club's calendar is Sao Paulo's, in one place: billing months and reconciliation days must agree on where midnight is.
public static class ClubeCalendar
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    // The first instant of the day in Sao Paulo, as an absolute moment.
    public static DateTimeOffset StartOf(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight));
    }
}
