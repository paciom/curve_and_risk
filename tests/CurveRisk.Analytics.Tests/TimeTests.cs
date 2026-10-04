using CurveRisk.Analytics.Time;

namespace CurveRisk.Analytics.Tests;

public class TimeTests
{
    private static DateTime D(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(DayCount.Act360, "2026-01-15", "2026-07-15", 181.0 / 360)]
    [InlineData(DayCount.Act365Fixed, "2026-01-15", "2027-01-15", 1.0)]
    [InlineData(DayCount.Act365Fixed, "2028-01-15", "2029-01-15", 366.0 / 365)]
    [InlineData(DayCount.Thirty360, "2026-01-15", "2026-07-15", 0.5)]
    [InlineData(DayCount.Thirty360, "2026-01-31", "2026-03-31", 60.0 / 360)]
    [InlineData(DayCount.Thirty360, "2026-01-30", "2026-02-28", 28.0 / 360)]
    [InlineData(DayCount.Thirty360, "2026-02-15", "2026-03-31", 46.0 / 360)]
    public void Year_fractions_follow_each_convention(DayCount dayCount, string start, string end, double expected)
    {
        Assert.Equal(expected, dayCount.YearFraction(Parse(start), Parse(end)), precision: 14);
    }

    private static DateTime Parse(string date) =>
        DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void A_year_fraction_backwards_in_time_is_negative_and_an_unknown_convention_is_rejected()
    {
        Assert.Equal(-1.0, DayCount.Act365Fixed.YearFraction(D(2027, 1, 15), D(2026, 1, 15)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((DayCount)99).YearFraction(D(2026, 1, 1), D(2026, 2, 1)));
    }

    [Theory]
    [InlineData(2026, 1, 1)]    // New Year
    [InlineData(2026, 1, 19)]   // Martin Luther King Jr. Day, third Monday
    [InlineData(2026, 2, 16)]   // Presidents Day
    [InlineData(2026, 5, 25)]   // Memorial Day: fourth and last Monday of a four-Monday May
    [InlineData(2027, 5, 31)]   // Memorial Day: fifth Monday
    [InlineData(2026, 6, 19)]   // Juneteenth
    [InlineData(2026, 7, 3)]    // Independence Day falls on Saturday, observed Friday
    [InlineData(2027, 7, 5)]    // Independence Day falls on Sunday, observed Monday
    [InlineData(2026, 9, 7)]    // Labor Day
    [InlineData(2026, 10, 12)]  // Columbus Day
    [InlineData(2026, 11, 11)]  // Veterans Day
    [InlineData(2026, 11, 26)]  // Thanksgiving
    [InlineData(2026, 12, 25)]  // Christmas
    [InlineData(2023, 1, 2)]    // New Year 2023 falls on Sunday, observed Monday
    public void Us_holidays_are_not_business_days(int year, int month, int day) =>
        Assert.False(BusinessCalendar.UnitedStates.IsBusinessDay(D(year, month, day)));

    [Theory]
    [InlineData(2026, 9, 30)]   // ordinary Wednesday
    [InlineData(2027, 5, 24)]   // fourth Monday of a five-Monday May is not Memorial Day
    [InlineData(2026, 7, 6)]    // the Monday after a Saturday holiday is a working day
    [InlineData(2026, 11, 19)]  // third Thursday of November
    [InlineData(2026, 12, 24)]  // Christmas Eve
    [InlineData(2027, 12, 31)]  // New Year 2028 falls on Saturday: year-end stays a business day
    [InlineData(2028, 1, 3)]    // and the Monday after is not taken instead
    public void Ordinary_weekdays_are_business_days(int year, int month, int day) =>
        Assert.True(BusinessCalendar.UnitedStates.IsBusinessDay(D(year, month, day)));

    [Fact]
    public void Adjustment_follows_the_convention()
    {
        var calendar = BusinessCalendar.WeekendsOnly;
        var saturday = D(2026, 10, 31);

        Assert.Equal(saturday, calendar.Adjust(saturday, BusinessDayConvention.Unadjusted));
        Assert.Equal(D(2026, 11, 2), calendar.Adjust(saturday, BusinessDayConvention.Following));
        Assert.Equal(D(2026, 10, 30), calendar.Adjust(saturday, BusinessDayConvention.ModifiedFollowing));
        Assert.Equal(D(2026, 10, 12), calendar.Adjust(D(2026, 10, 10), BusinessDayConvention.ModifiedFollowing));
        Assert.Equal(D(2026, 10, 14), calendar.Adjust(D(2026, 10, 14), BusinessDayConvention.Following));
        Assert.Equal("Weekends", calendar.Name);
    }

    [Fact]
    public void Business_days_are_added_and_subtracted_across_weekends_and_holidays()
    {
        var us = BusinessCalendar.UnitedStates;

        Assert.Equal(D(2026, 10, 2), us.AddBusinessDays(D(2026, 9, 30), 2));
        Assert.Equal(D(2026, 7, 7), us.AddBusinessDays(D(2026, 7, 2), 2));
        Assert.Equal(D(2026, 9, 28), us.AddBusinessDays(D(2026, 9, 30), -2));
        Assert.Equal(D(2026, 9, 30), us.AddBusinessDays(D(2026, 9, 30), 0));
    }

    [Theory]
    [InlineData("2D", 2, TenorUnit.Days, "2D")]
    [InlineData("1w", 1, TenorUnit.Weeks, "1W")]
    [InlineData(" 6M ", 6, TenorUnit.Months, "6M")]
    [InlineData("10Y", 10, TenorUnit.Years, "10Y")]
    public void Tenors_parse_and_print(string text, int count, TenorUnit unit, string printed)
    {
        var tenor = Tenor.Parse(text);

        Assert.Equal(new Tenor(count, unit), tenor);
        Assert.Equal(printed, tenor.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Y")]
    [InlineData("0M")]
    [InlineData("-3M")]
    [InlineData("3X")]
    [InlineData("M3")]
    public void Malformed_tenors_are_rejected(string text) => Assert.Throws<FormatException>(() => Tenor.Parse(text));

    [Fact]
    public void Tenors_add_calendar_time_and_clamp_at_month_end()
    {
        Assert.Throws<ArgumentNullException>(() => Tenor.Parse(null!));
        Assert.Equal(D(2026, 2, 28), Tenor.Months(1).AddTo(D(2026, 1, 31)));
        Assert.Equal(D(2031, 9, 30), Tenor.Years(5).AddTo(D(2026, 9, 30)));
        Assert.Equal(D(2026, 10, 14), new Tenor(2, TenorUnit.Weeks).AddTo(D(2026, 9, 30)));
        Assert.Equal(D(2026, 10, 3), new Tenor(3, TenorUnit.Days).AddTo(D(2026, 9, 30)));
        Assert.Throws<InvalidOperationException>(() => new Tenor(1, (TenorUnit)42).AddTo(D(2026, 1, 1)));
    }
}
