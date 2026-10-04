using CurveRisk.Analytics.Curves;

namespace CurveRisk.Analytics.Tests;

/// <summary>
/// Interpolated values on grids with unequal spacing, against numbers worked by hand. Nodes are the
/// origin and pillars at 1, 3 and 4 years; the expected log discount factors are exact fractions.
/// </summary>
public class InterpolationTests
{
    private static readonly DateTime AsOf = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTime[] Pillars = [AsOf.AddDays(365), AsOf.AddDays(1095), AsOf.AddDays(1460)];

    private static DiscountCurve Curve(InterpolationScheme scheme, params double[] logDiscounts) =>
        new(AsOf, Pillars, [.. logDiscounts.Select(Math.Exp)], scheme);

    // Secants are -0.03, -0.06 and -0.04 over widths 1, 2 and 1. The Fritsch-Carlson slope at 1Y is
    // the harmonic mean of the first two secants with weights 5 and 4, which is -27/700, and at 3Y
    // that of the last two with weights 4 and 5, which is -27/575. The end slopes are the end
    // secants. Each expected value is the cubic Hermite through those slopes.
    [Theory]
    [InlineData(0.5, -0.013928571428571429)]   // -0.015 + (-0.03 + 27/700) / 8
    [InlineData(2.0, -0.08790372670807453)]    // -0.09 + (27/575 - 27/700) / 4
    [InlineData(3.5, -0.1708695652173913)]     // -0.17 + (-27/575 + 0.04) / 8
    public void The_monotone_cubic_weights_neighbouring_secants_by_the_widths_of_their_segments(double time, double expectedLogDiscount)
    {
        var curve = Curve(InterpolationScheme.MonotoneCubicLogDiscount, -0.03, -0.15, -0.19);

        var logDiscount = Math.Log(curve.DiscountFactor(time));

        Assert.Equal(expectedLogDiscount, logDiscount, precision: 14);
    }

    // The log discount factor falls, rises, then falls, so the slope is zero at both inner pillars.
    // Between two flat nodes the cubic is at the average of their values half way along; in the first
    // segment it starts with the secant slope -0.03 and ends flat.
    [Theory]
    [InlineData(2.0, -0.02)]        // (-0.03 + -0.01) / 2
    [InlineData(0.5, -0.01875)]     // -0.015 + (-0.03) / 8
    public void The_monotone_cubic_is_flat_at_a_pillar_where_the_data_turns(double time, double expectedLogDiscount)
    {
        var curve = Curve(InterpolationScheme.MonotoneCubicLogDiscount, -0.03, -0.01, -0.05);

        var logDiscount = Math.Log(curve.DiscountFactor(time));

        Assert.Equal(expectedLogDiscount, logDiscount, precision: 14);
    }

    [Fact]
    public void Linear_zero_holds_the_first_pillars_zero_rate_before_a_first_pillar_that_is_not_at_one_year()
    {
        // The first pillar is at 3 years with log discount factor -0.15: a 5% zero rate.
        var curve = new DiscountCurve(AsOf, [AsOf.AddDays(1095), AsOf.AddDays(1460)], [Math.Exp(-0.15), Math.Exp(-0.24)], InterpolationScheme.LinearZero);

        Assert.Equal(0.05, curve.ZeroRate(1.0), precision: 14);
        Assert.Equal(0.05, curve.ZeroRate(3.0), precision: 14);
        Assert.Equal(0.055, curve.ZeroRate(3.5), precision: 14);
    }

    [Fact]
    public void An_unknown_scheme_is_named_as_such_when_the_curve_is_read()
    {
        var curve = Curve((InterpolationScheme)9, -0.03, -0.15, -0.19);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => curve.DiscountFactor(1.5));

        Assert.StartsWith("Unknown interpolation scheme.", ex.Message, StringComparison.Ordinal);
        Assert.Equal("scheme", ex.ParamName);
    }

    [Fact]
    public void A_zero_rate_at_time_zero_is_refused_with_the_reason()
    {
        var curve = Curve(InterpolationScheme.LogLinearDiscount, -0.03, -0.15, -0.19);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => curve.ZeroRate(0));

        Assert.StartsWith("A zero rate needs a positive time.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_curve_without_one_discount_factor_per_pillar_says_what_it_needs()
    {
        var ex = Assert.Throws<ArgumentException>(() => new DiscountCurve(AsOf, Pillars, [0.9, 0.8], InterpolationScheme.LogLinearDiscount));

        Assert.StartsWith("A curve needs at least one pillar and one discount factor per pillar.", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 10)]     // first pillar on the as-of date
    [InlineData(10, 10)]    // the same pillar twice
    [InlineData(20, 10)]    // out of order
    public void A_curve_whose_pillars_are_not_after_the_as_of_date_and_increasing_says_how_they_must_be_ordered(int firstDays, int secondDays)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new DiscountCurve(AsOf, [AsOf.AddDays(firstDays), AsOf.AddDays(secondDays)], [0.9, 0.8], InterpolationScheme.LogLinearDiscount));

        Assert.StartsWith("Pillar dates must be after the as-of date and strictly increasing.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_curve_with_a_zero_discount_factor_says_what_factors_are_allowed()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new DiscountCurve(AsOf, [AsOf.AddDays(10)], [0.0], InterpolationScheme.LogLinearDiscount));

        Assert.StartsWith("Discount factors must be positive and finite.", ex.Message, StringComparison.Ordinal);
    }
}
