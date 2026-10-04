using CurveRisk.Analytics.Numerics;
using CurveRisk.Analytics.Time;

namespace CurveRisk.Analytics.Tests;

public class ScheduleAndSolverTests
{
    private static DateTime D(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

    private static ScheduleSpec Spec(DateTime effective, DateTime maturity, int months) =>
        new(effective, maturity, months, DayCount.Act360, BusinessCalendar.WeekendsOnly, BusinessDayConvention.ModifiedFollowing);

    [Fact]
    public void A_regular_schedule_has_one_period_per_payment_and_contiguous_dates()
    {
        var periods = Schedule.Build(Spec(D(2026, 10, 2), D(2028, 10, 2), 12));

        Assert.Equal(2, periods.Count);
        Assert.Equal(D(2026, 10, 2), periods[0].AccrualStart);
        Assert.Equal(periods[0].AccrualEnd, periods[1].AccrualStart);
        Assert.Equal(D(2028, 10, 2), periods[1].AccrualEnd);
        Assert.All(periods, period => Assert.Equal(period.AccrualEnd, period.PaymentDate));
        Assert.Equal(DayCount.Act360.YearFraction(D(2026, 10, 2), D(2027, 10, 4)), periods[0].YearFraction);
    }

    [Fact]
    public void An_irregular_schedule_puts_the_stub_first_and_adjusts_non_business_days()
    {
        // 15 March 2026 is a Sunday.
        var periods = Schedule.Build(Spec(D(2026, 3, 15), D(2028, 1, 14), 12));

        Assert.Equal(2, periods.Count);
        Assert.Equal(D(2026, 3, 16), periods[0].AccrualStart);
        Assert.Equal(D(2027, 1, 14), periods[0].AccrualEnd);
        Assert.True(periods[0].YearFraction < periods[1].YearFraction);
    }

    [Fact]
    public void A_month_end_maturity_does_not_drift_as_it_rolls_back()
    {
        var periods = Schedule.Build(Spec(D(2026, 2, 27), D(2027, 8, 31), 6));

        Assert.Equal([D(2026, 8, 31), D(2027, 2, 26), D(2027, 8, 31)], periods.Select(period => period.AccrualEnd));
    }

    [Fact]
    public void A_schedule_shorter_than_one_period_is_a_single_stub()
    {
        var period = Assert.Single(Schedule.Build(Spec(D(2026, 10, 2), D(2027, 1, 4), 12)));

        Assert.Equal((D(2026, 10, 2), D(2027, 1, 4)), (period.AccrualStart, period.AccrualEnd));
    }

    [Fact]
    public void Invalid_schedules_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Schedule.Build(Spec(D(2026, 10, 2), D(2026, 10, 2), 12)));
        Assert.Throws<ArgumentException>(() => Schedule.Build(Spec(D(2027, 10, 2), D(2026, 10, 2), 12)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Schedule.Build(Spec(D(2026, 10, 2), D(2027, 10, 2), 0)));
    }

    [Theory]
    [InlineData(0.0, 2.0, 1.4142135623730951)]
    [InlineData(-2.0, 0.0, -1.4142135623730951)]
    public void Brent_finds_the_root_inside_the_bracket(double lower, double upper, double expected) =>
        Assert.Equal(expected, Brent.Solve(x => (x * x) - 2, lower, upper), precision: 12);

    [Fact]
    public void Brent_handles_awkward_functions()
    {
        Assert.Equal(Math.Log(3), Brent.Solve(x => Math.Exp(x) - 3, -5, 10), precision: 12);
        Assert.Equal(2.0, Brent.Solve(x => Math.Pow(x - 2, 3), 0, 5), precision: 4);
        Assert.Equal(0.7390851332151607, Brent.Solve(x => Math.Cos(x) - x, 0, 1), precision: 12);
    }

    [Fact]
    public void A_root_of_large_magnitude_converges_although_the_absolute_tolerance_is_below_double_spacing()
    {
        Assert.Equal(Math.Log(1e30), Brent.Solve(x => Math.Exp(x) - 1e30, 0, 100), precision: 10);
    }

    [Fact]
    public void A_function_that_is_not_a_number_at_the_bracket_is_refused_at_once()
    {
        var evaluations = 0;

        var ex = Assert.Throws<RootFindingException>(() => Brent.Solve(
            _ =>
            {
                evaluations++;
                return double.NaN;
            },
            0,
            1));

        Assert.Contains("not a number", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, evaluations);
    }

    [Fact]
    public void A_root_at_an_end_of_the_bracket_is_returned()
    {
        Assert.Equal(1.0, Brent.Solve(x => x - 1, 1, 3), precision: 14);
        Assert.Equal(3.0, Brent.Solve(x => x - 3, 1, 3), precision: 14);
    }

    [Fact]
    public void A_bracket_without_a_sign_change_is_reported_with_both_values()
    {
        var ex = Assert.Throws<RootFindingException>(() => Brent.Solve(x => (x * x) + 1, -1, 1));

        Assert.Contains("not bracketed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("f(lower)=2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sign_change_without_a_root_converges_on_the_jump_so_callers_must_check_the_residual()
    {
        // A bracketing method finds where the sign changes. For a step that is the discontinuity, not
        // a root; calibration guards against this by checking every residual after solving.
        var jump = Brent.Solve(x => x < 0.3 ? -1 : 1, 0, 1);

        Assert.Equal(0.3, jump, precision: 12);
    }
}
