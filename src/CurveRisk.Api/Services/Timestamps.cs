namespace CurveRisk.Api.Services;

public static class Timestamps
{
    /// <summary>
    /// The current instant, to the millisecond. .NET measures in 100-nanosecond ticks and PostgreSQL
    /// stores microseconds, so an untruncated value would read back different from what was returned
    /// when it was created.
    /// </summary>
    public static DateTimeOffset UtcNowToMillisecond(this TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }
}
