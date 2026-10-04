namespace CurveRisk.Analytics.Time;

/// <summary>One accrual period of a leg.</summary>
/// <param name="AccrualStart">Adjusted start of interest accrual.</param>
/// <param name="AccrualEnd">Adjusted end of interest accrual.</param>
/// <param name="PaymentDate">When the cash moves. Here equal to <paramref name="AccrualEnd"/> (no payment lag).</param>
/// <param name="YearFraction">Accrual time under the leg's day count.</param>
public sealed record Period(DateTime AccrualStart, DateTime AccrualEnd, DateTime PaymentDate, double YearFraction);

/// <summary>Everything needed to lay out a leg's periods. No convention is defaulted.</summary>
/// <param name="Effective">Unadjusted start of the first period.</param>
/// <param name="Maturity">Unadjusted end of the last period.</param>
/// <param name="FrequencyMonths">Months between payments: 12 annual, 6 semi-annual, 3 quarterly.</param>
public sealed record ScheduleSpec(
    DateTime Effective,
    DateTime Maturity,
    int FrequencyMonths,
    DayCount DayCount,
    BusinessCalendar Calendar,
    BusinessDayConvention Convention);

public static class Schedule
{
    /// <summary>
    /// Generates periods backward from maturity, so any irregular (stub) period is the first one,
    /// which is the market default for swaps.
    /// </summary>
    public static IReadOnlyList<Period> Build(ScheduleSpec spec)
    {
        Validate(spec);

        // Each boundary is stepped from maturity, not from the previous boundary, so a month-end
        // maturity does not drift (31st, 30th, 30th...) as it walks back through shorter months.
        var boundaries = new List<DateTime> { spec.Maturity.Date };
        var step = 1;
        var date = spec.Maturity.Date.AddMonths(-spec.FrequencyMonths);
        while (date > spec.Effective.Date)
        {
            boundaries.Add(date);
            step++;
            date = spec.Maturity.Date.AddMonths(-step * spec.FrequencyMonths);
        }

        boundaries.Add(spec.Effective.Date);
        boundaries.Reverse();

        // Two boundaries a day or two apart can adjust onto the same business day; a period of no
        // length is not a period.
        var adjusted = boundaries.Select(boundary => spec.Calendar.Adjust(boundary, spec.Convention)).Distinct().ToList();
        return adjusted
            .Zip(adjusted.Skip(1), (start, end) => new Period(start, end, end, spec.DayCount.YearFraction(start, end)))
            .ToList();
    }

    private static void Validate(ScheduleSpec spec)
    {
        if (spec.Maturity.Date <= spec.Effective.Date)
        {
            throw new ArgumentException($"Maturity {spec.Maturity:yyyy-MM-dd} must be after the effective date {spec.Effective:yyyy-MM-dd}.", nameof(spec));
        }

        if (spec.FrequencyMonths < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), spec.FrequencyMonths, "Payment frequency must be at least one month.");
        }
    }
}
