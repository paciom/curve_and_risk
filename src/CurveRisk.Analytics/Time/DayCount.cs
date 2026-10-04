namespace CurveRisk.Analytics.Time;

/// <summary>How the time between two dates is measured for interest accrual.</summary>
public enum DayCount
{
    /// <summary>Actual days over 360. USD money market and SOFR legs.</summary>
    Act360,

    /// <summary>Actual days over 365. Used here as curve time.</summary>
    Act365Fixed,

    /// <summary>30/360 US bond basis: every month counts as 30 days.</summary>
    Thirty360,
}

public static class DayCountExtensions
{
    /// <summary>Year fraction from <paramref name="start"/> to <paramref name="end"/>; negative when end is earlier.</summary>
    public static double YearFraction(this DayCount dayCount, DateTime start, DateTime end) => dayCount switch
    {
        DayCount.Act360 => (end.Date - start.Date).TotalDays / 360.0,
        DayCount.Act365Fixed => (end.Date - start.Date).TotalDays / 365.0,
        DayCount.Thirty360 => Thirty360Days(start, end) / 360.0,
        _ => throw new ArgumentOutOfRangeException(nameof(dayCount), dayCount, "Unknown day count."),
    };

    // US (NASD) rule without the end-of-February adjustment: a start on the 31st counts as the 30th,
    // and an end on the 31st counts as the 30th only when the start is on or after the 30th.
    private static int Thirty360Days(DateTime start, DateTime end)
    {
        var startDay = Math.Min(start.Day, 30);
        var endDay = end.Day == 31 && startDay == 30 ? 30 : end.Day;
        return ((end.Year - start.Year) * 360) + ((end.Month - start.Month) * 30) + (endDay - startDay);
    }
}
