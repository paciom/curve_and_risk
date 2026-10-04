namespace CurveRisk.Analytics.Time;

/// <summary>What to do when a date falls on a non-business day.</summary>
public enum BusinessDayConvention
{
    Unadjusted,

    /// <summary>Roll forward to the next business day.</summary>
    Following,

    /// <summary>Roll forward, unless that crosses into the next month; then roll back.</summary>
    ModifiedFollowing,
}

/// <summary>Weekends plus a holiday rule.</summary>
public sealed class BusinessCalendar(string name, Func<DateTime, bool> isHoliday)
{
    /// <summary>Saturdays and Sundays only. Useful for tests and as a neutral default.</summary>
    public static BusinessCalendar WeekendsOnly { get; } = new("Weekends", _ => false);

    /// <summary>US federal holidays, the basis of the SOFR publication calendar.</summary>
    public static BusinessCalendar UnitedStates { get; } = new("US", UsHolidays.IsHoliday);

    public string Name { get; } = name;

    public bool IsBusinessDay(DateTime date) =>
        date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday && !isHoliday(date.Date);

    public DateTime Adjust(DateTime date, BusinessDayConvention convention)
    {
        if (convention == BusinessDayConvention.Unadjusted)
        {
            return date.Date;
        }

        var following = Roll(date.Date, step: 1);
        return convention == BusinessDayConvention.ModifiedFollowing && following.Month != date.Month
            ? Roll(date.Date, step: -1)
            : following;
    }

    /// <summary>Moves forward (or back, for a negative count) by whole business days.</summary>
    public DateTime AddBusinessDays(DateTime date, int count)
    {
        var step = Math.Sign(count);
        var result = date.Date;
        for (var remaining = Math.Abs(count); remaining > 0; remaining--)
        {
            result = Roll(result.AddDays(step), step);
        }

        return result;
    }

    private DateTime Roll(DateTime date, int step)
    {
        while (!IsBusinessDay(date))
        {
            date = date.AddDays(step);
        }

        return date;
    }
}
