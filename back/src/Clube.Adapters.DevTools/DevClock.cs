namespace Clube.Adapters.DevTools;

// Exists so a demo can show the 12-month credit expiry and the billing window without waiting a year: it is the real
// (or, in tests, the fake) clock plus a number of days. Only ever registered in Development; timers stay on the base
// clock, so background loops keep their real pace and only "what day is it" moves.
public sealed class DevClock(TimeProvider baseClock) : TimeProvider
{
    public const int MaxOffsetDays = 3650;

    private readonly Lock gate = new();
    private int offsetDays;

    public int OffsetDays
    {
        get
        {
            lock (gate) return offsetDays;
        }
    }

    public override DateTimeOffset GetUtcNow() => baseClock.GetUtcNow().AddDays(OffsetDays);

    public override long GetTimestamp() => baseClock.GetTimestamp();

    public override long TimestampFrequency => baseClock.TimestampFrequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        baseClock.CreateTimer(callback, state, dueTime, period);

    // False when the shift would leave the supported range; the clock is untouched then.
    public bool TryAdvance(int days)
    {
        lock (gate)
        {
            if (days is < 1 or > MaxOffsetDays || offsetDays + days > MaxOffsetDays) return false;

            offsetDays += days;
            return true;
        }
    }

    public void Reset()
    {
        lock (gate) offsetDays = 0;
    }
}
