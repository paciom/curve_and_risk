using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Time;

namespace CurveRisk.Analytics.Tests;

/// <summary>Instruments and schedules on the exact dates where their rules change over.</summary>
public class BoundaryDateTests
{
    private static readonly DateTime AsOf = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>A flat 4% continuously compounded curve.</summary>
    private static readonly DiscountCurve Flat = new DiscountCurve(
        AsOf, [AsOf.AddDays(3650)], [1.0], InterpolationScheme.LogLinearDiscount).ShiftZeroRates(_ => 0.04);

    private static ScheduleSpec Spec(DateTime effective, DateTime maturity, int months, DayCount dayCount) =>
        new(effective, maturity, months, dayCount, BusinessCalendar.WeekendsOnly, BusinessDayConvention.Unadjusted);

    /// <summary>A 5% annual 30/360 bond with a face value of one million.</summary>
    private static FixedRateBond Bond(DateTime issue, int years) =>
        new(1_000_000, 0.05, DayCount.Thirty360, Schedule.Build(Spec(issue, issue.AddYears(years), 12, DayCount.Thirty360)));

    [Fact]
    public void A_bond_maturing_on_the_curve_date_has_already_repaid_its_principal()
    {
        var bond = Bond(AsOf.AddYears(-2), years: 2);

        Assert.Equal(AsOf, bond.Maturity);
        Assert.Equal(0.0, bond.PresentValue(Flat));
    }

    [Fact]
    public void On_a_coupon_date_the_coupon_just_paid_no_longer_accrues()
    {
        var bond = Bond(AsOf.AddYears(-1), years: 2);

        Assert.Equal(AsOf, bond.Coupons[0].AccrualEnd);
        Assert.Equal(0.0, bond.AccruedInterest(AsOf));
    }

    [Fact]
    public void Nothing_accrues_at_maturity_or_before_issue()
    {
        var bond = Bond(AsOf, years: 2);

        Assert.Equal(0.0, bond.AccruedInterest(AsOf.AddYears(2)));
        Assert.Equal(0.0, bond.AccruedInterest(AsOf.AddDays(-1)));
    }

    [Fact]
    public void A_swap_starting_on_the_curve_date_is_valued_as_unstarted()
    {
        var end = AsOf.AddDays(365);
        var swap = new InterestRateSwap(1_000_000, 0.03, PayFixed: true, [new Period(AsOf, end, end, 365.0 / 360)]);

        // One period: annuity = accrual x exp(-0.04 x 1), par = (1 - DF) / annuity.
        Assert.Equal(365.0 / 360 * Math.Exp(-0.04), swap.Annuity(Flat), precision: 14);
        Assert.Equal((Math.Exp(0.04) - 1) * 360 / 365, swap.ParRate(Flat), precision: 14);
    }

    [Fact]
    public void A_monthly_schedule_is_the_shortest_frequency_allowed()
    {
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var periods = Schedule.Build(Spec(start, start.AddMonths(3), 1, DayCount.Thirty360));

        Assert.Equal([start.AddMonths(1), start.AddMonths(2), start.AddMonths(3)], periods.Select(period => period.AccrualEnd));
        Assert.All(periods, period => Assert.Equal(30.0 / 360, period.YearFraction, precision: 14));
    }

    [Fact]
    public void A_schedule_that_ends_when_it_starts_is_refused_with_both_dates()
    {
        var day = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Unspecified);

        var ex = Assert.Throws<ArgumentException>(() => Schedule.Build(Spec(day, day, 12, DayCount.Act360)));

        Assert.StartsWith("Maturity 2026-10-02 must be after the effective date 2026-10-02.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schedule_with_no_months_between_payments_is_refused_with_the_minimum()
    {
        var day = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Unspecified);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Schedule.Build(Spec(day, day.AddYears(1), 0, DayCount.Act360)));

        Assert.StartsWith("Payment frequency must be at least one month.", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, ex.ActualValue);
    }

    [Theory]
    [InlineData("3X")]
    [InlineData("3H")]
    public void A_tenor_with_an_unknown_unit_is_refused_with_the_units_allowed(string text)
    {
        var ex = Assert.Throws<FormatException>(() => Tenor.Parse(text));

        Assert.Equal($"'{text}' has an unknown tenor unit. Use D, W, M or Y.", ex.Message);
    }

    [Fact]
    public void A_tenor_with_an_undefined_unit_cannot_be_added_to_a_date()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new Tenor(1, (TenorUnit)42).AddTo(AsOf));

        Assert.Equal("Unknown tenor unit 42.", ex.Message);
    }

    [Fact]
    public void An_undefined_day_count_is_named_as_unknown()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ((DayCount)99).YearFraction(AsOf, AsOf.AddDays(30)));

        Assert.StartsWith("Unknown day count.", ex.Message, StringComparison.Ordinal);
    }
}
