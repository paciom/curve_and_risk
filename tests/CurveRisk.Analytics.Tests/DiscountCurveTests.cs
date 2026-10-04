using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Time;

namespace CurveRisk.Analytics.Tests;

public class DiscountCurveTests
{
    private static readonly DateTime AsOf = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTime[] Pillars = [AsOf.AddDays(365), AsOf.AddDays(730), AsOf.AddDays(1825), AsOf.AddDays(3650)];
    private static readonly double[] Factors = [0.96, 0.925, 0.83, 0.67];

    public static TheoryData<InterpolationScheme> Schemes =>
        [InterpolationScheme.LogLinearDiscount, InterpolationScheme.LinearZero, InterpolationScheme.MonotoneCubicLogDiscount];

    private static DiscountCurve Curve(InterpolationScheme scheme) => new(AsOf, Pillars, Factors, scheme);

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Every_scheme_reproduces_the_pillars_and_is_one_today_and_in_the_past(InterpolationScheme scheme)
    {
        var curve = Curve(scheme);

        for (var i = 0; i < Pillars.Length; i++)
        {
            Assert.Equal(Factors[i], curve.DiscountFactor(Pillars[i]), precision: 14);
        }

        Assert.Equal(1.0, curve.DiscountFactor(AsOf));
        Assert.Equal(1.0, curve.DiscountFactor(AsOf.AddDays(-30)));
        Assert.Equal([1.0, 2.0, 5.0, 10.0], curve.PillarTimes);
        Assert.Equal(scheme, curve.Scheme);
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Discount_factors_decrease_with_time_and_the_zero_rate_is_flat_beyond_the_last_pillar(InterpolationScheme scheme)
    {
        var curve = Curve(scheme);

        var grid = Enumerable.Range(1, 600).Select(i => i * 0.02).Select(curve.DiscountFactor).ToList();
        Assert.All(grid.Zip(grid.Skip(1), (earlier, later) => later < earlier), Assert.True);

        Assert.Equal(curve.ZeroRate(10.0), curve.ZeroRate(25.0), precision: 14);
        Assert.Equal(-Math.Log(0.67) / 10.0, curve.ZeroRate(10.0), precision: 14);
    }

    [Fact]
    public void Log_linear_interpolation_is_linear_in_log_discount_including_from_the_origin()
    {
        var curve = Curve(InterpolationScheme.LogLinearDiscount);

        Assert.Equal(Math.Sqrt(0.96 * 0.925), curve.DiscountFactor(1.5), precision: 14);
        Assert.Equal(Math.Sqrt(0.96), curve.DiscountFactor(0.5), precision: 14);
    }

    [Fact]
    public void Linear_zero_interpolation_averages_zero_rates_and_is_flat_before_the_first_pillar()
    {
        var curve = Curve(InterpolationScheme.LinearZero);
        var zero1 = -Math.Log(0.96);
        var zero2 = -Math.Log(0.925) / 2;

        Assert.Equal((zero1 + zero2) / 2, curve.ZeroRate(1.5), precision: 14);
        Assert.Equal(zero1, curve.ZeroRate(0.25), precision: 14);
    }

    [Fact]
    public void The_monotone_cubic_is_smoother_than_log_linear_where_the_slope_changes()
    {
        var cubic = Curve(InterpolationScheme.MonotoneCubicLogDiscount);
        var linear = Curve(InterpolationScheme.LogLinearDiscount);

        // Instantaneous forward either side of the 2Y pillar: log-linear jumps, the cubic does not.
        static double Forward(DiscountCurve curve, double t) => Math.Log(curve.DiscountFactor(t) / curve.DiscountFactor(t + 1e-4)) / 1e-4;

        Assert.True(Math.Abs(Forward(linear, 2.001) - Forward(linear, 1.998)) > 1e-3);
        Assert.True(Math.Abs(Forward(cubic, 2.001) - Forward(cubic, 1.998)) < 1e-4);
        Assert.NotEqual(linear.DiscountFactor(1.5), cubic.DiscountFactor(1.5));
    }

    [Fact]
    public void A_cubic_through_data_that_turns_does_not_overshoot()
    {
        // Discount factors that rise then fall: the interpolant must stay within neighbouring pillar values.
        var curve = new DiscountCurve(AsOf, Pillars, [0.96, 0.97, 0.90, 0.80], InterpolationScheme.MonotoneCubicLogDiscount);

        var between = Enumerable.Range(0, 101).Select(i => curve.DiscountFactor(1.0 + (i * 0.01))).ToList();

        Assert.All(between, factor => Assert.InRange(factor, 0.96 - 1e-12, 0.97 + 1e-12));
    }

    [Fact]
    public void Forward_rates_and_zero_rates_are_consistent_with_discount_factors()
    {
        var curve = Curve(InterpolationScheme.LogLinearDiscount);

        var forward = curve.ForwardRate(Pillars[0], Pillars[1], DayCount.Act360);

        Assert.Equal(((0.96 / 0.925) - 1) / (365.0 / 360), forward, precision: 14);
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ZeroRate(0));
        Assert.Equal(2.0, curve.TimeTo(Pillars[1]));
    }

    [Fact]
    public void Shifts_and_bumps_move_zero_rates_by_exactly_the_stated_amount_and_leave_the_original_unchanged()
    {
        var curve = Curve(InterpolationScheme.LogLinearDiscount);

        var shifted = curve.ShiftZeroRates(_ => 0.0010);
        var bumped = curve.BumpPillar(2, 0.0001);

        Assert.All(curve.PillarTimes, time => Assert.Equal(curve.ZeroRate(time) + 0.0010, shifted.ZeroRate(time), precision: 14));
        Assert.Equal(curve.ZeroRate(5.0) + 0.0001, bumped.ZeroRate(5.0), precision: 14);
        Assert.Equal(curve.ZeroRate(2.0), bumped.ZeroRate(2.0), precision: 14);
        Assert.Equal(curve.ZeroRate(10.0), bumped.ZeroRate(10.0), precision: 14);
        Assert.Equal(Factors, curve.DiscountFactors);
    }

    [Fact]
    public void Invalid_curves_are_rejected_at_construction()
    {
        var scheme = InterpolationScheme.LogLinearDiscount;

        Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, [], [], scheme));
        Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, Pillars, [0.9, 0.8], scheme));
        Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, [AsOf, AsOf.AddDays(10)], [0.9, 0.8], scheme));
        Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, [AsOf.AddDays(20), AsOf.AddDays(10)], [0.9, 0.8], scheme));
        Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, [AsOf.AddDays(10)], [0.0], scheme));
        Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, [AsOf.AddDays(10)], [double.NaN], scheme));
        Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, [AsOf.AddDays(10)], [double.PositiveInfinity], scheme));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscountCurve(AsOf, Pillars, Factors, (InterpolationScheme)9).DiscountFactor(1.5));
    }
}
