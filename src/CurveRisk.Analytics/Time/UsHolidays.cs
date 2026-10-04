namespace CurveRisk.Analytics.Time;

/// <summary>
/// US holidays by rule. A fixed-date holiday on a Sunday is observed the Monday after, and one on a
/// Saturday the Friday before, except New Year's Day: 31 December stays a business day, as it is the
/// year-end settlement date. Good Friday and one-off closures are not modelled, and the market's
/// treatment of Veterans Day on a Saturday is not verified here.
/// </summary>
internal static class UsHolidays
{
    private static readonly (int Month, int Day)[] FixedDates = [(1, 1), (6, 19), (7, 4), (11, 11), (12, 25)];

    // Month, weekday, occurrence within the month (5 means last).
    private static readonly (int Month, DayOfWeek Day, int Occurrence)[] Floating =
    [
        (1, DayOfWeek.Monday, 3),     // Martin Luther King Jr. Day
        (2, DayOfWeek.Monday, 3),     // Presidents Day
        (5, DayOfWeek.Monday, 5),     // Memorial Day
        (9, DayOfWeek.Monday, 1),     // Labor Day
        (10, DayOfWeek.Monday, 2),    // Columbus Day
        (11, DayOfWeek.Thursday, 4),  // Thanksgiving
    ];

    public static bool IsHoliday(DateTime date) => IsObservedFixedDate(date) || IsFloating(date);

    private static bool IsObservedFixedDate(DateTime date)
    {
        // The observed day can be one day either side of the nominal date, including across a year end.
        var candidates = new[] { date, date.AddDays(1), date.AddDays(-1) };
        return candidates.Any(nominal => IsFixedDate(nominal) && Observed(nominal) == date.Date);
    }

    private static bool IsFixedDate(DateTime date) => FixedDates.Contains((date.Month, date.Day));

    /// <summary>The day the holiday is taken, or null when it falls on a Saturday and is not moved.</summary>
    private static DateTime? Observed(DateTime nominal)
    {
        var isNewYear = nominal.Month == 1 && nominal.Day == 1;
        return nominal.DayOfWeek switch
        {
            DayOfWeek.Saturday => isNewYear ? null : nominal.Date.AddDays(-1),
            DayOfWeek.Sunday => nominal.Date.AddDays(1),
            _ => nominal.Date,
        };
    }

    private static bool IsFloating(DateTime date) =>
        Floating.Any(rule => rule.Month == date.Month && rule.Day == date.DayOfWeek && Occurrence(date) == rule.Occurrence);

    private static int Occurrence(DateTime date)
    {
        var isLast = date.AddDays(7).Month != date.Month;
        var ordinal = ((date.Day - 1) / 7) + 1;

        // A date that is both the fourth and the last of its weekday (a four-Monday May) must match
        // "last" rules; rules for the fourth occurrence only ever ask about November Thursdays.
        return isLast && date.Month == 5 ? 5 : ordinal;
    }
}
