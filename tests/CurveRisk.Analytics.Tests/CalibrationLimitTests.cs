using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;

namespace CurveRisk.Analytics.Tests;

public class CalibrationLimitTests
{
    private static readonly DateTime AsOf = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void A_market_that_reprices_only_on_the_fiftieth_and_last_allowed_sweep_still_calibrates()
    {
        // The first instrument asks for DF(1Y) = 0.28 + 0.6 DF(2Y), the second for DF(2Y) = DF(1Y), so
        // both discount factors tend to 0.7 and each sweep leaves 0.6 of the previous error. Starting
        // from DF(2Y) = exp(-0.06), the first quote misses by 0.4 x 0.6^n x 0.2418 after n sweeps:
        // 1.3e-12 after 49, 7.8e-13 after 50, against a tolerance of 1e-12.
        var first = new Coupled(AsOf.AddDays(365), Own: 0, Other: 1, Weight: 0.6, Quote: 0.28);
        var second = new Coupled(AsOf.AddDays(730), Own: 1, Other: 0, Weight: 1.0, Quote: 0.0);

        var result = new CurveMarket(AsOf, [first, second], InterpolationScheme.LogLinearDiscount).Calibrate();

        Assert.Equal(50, result.Sweeps);
        Assert.Equal(0.7, result.Curve.DiscountFactors[0], precision: 11);
        Assert.Equal(0.7, result.Curve.DiscountFactors[1], precision: 11);
    }

    [Fact]
    public void An_empty_market_is_refused_with_what_a_curve_needs()
    {
        var ex = Assert.Throws<CalibrationException>(() => new CurveMarket(AsOf, [], InterpolationScheme.LogLinearDiscount).Calibrate());

        Assert.Equal("A curve needs at least one calibration instrument.", ex.Message);
    }

    /// <summary>Reprices when its own pillar's discount factor equals the quote plus a weight times another pillar's.</summary>
    private sealed record Coupled(DateTime PillarDate, int Own, int Other, double Weight, double Quote) : ICalibrationInstrument
    {
        public string Label => "Coupled";

        public double ModelQuote(DiscountCurve curve) => curve.DiscountFactors[Own] - (Weight * curve.DiscountFactors[Other]);

        public ICalibrationInstrument WithQuote(double quote) => this with { Quote = quote };
    }
}
