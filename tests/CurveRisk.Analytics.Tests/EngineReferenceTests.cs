using CurveRisk.Ai.Tools;
using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Risk;
using CurveRisk.Analytics.Time;
using CurveRisk.Engine;

namespace CurveRisk.Analytics.Tests;

/// <summary>
/// Absolute figures for the demo market. They were derived independently of this library by a
/// from-scratch Python bootstrap (own schedule, log-linear interpolation, bisection) during review,
/// and agree with the closed form (par - fixed) x annuity x notional = 0.0028 x 4.5359367 x 1e8.
/// Without pinned numbers a change that moves every valuation the same way would pass every
/// relative test in the suite.
/// </summary>
public class EngineReferenceTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly AnalyticsRiskEngine Engine = AnalyticsRiskEngine.CreateDemo();

    [Fact]
    public async Task T1001_values_match_the_independent_derivation()
    {
        var valuation = await Engine.PriceTradeAsync("T-1001", Ct);
        var risk = await Engine.RunRiskAsync("T-1001", Ct);

        Assert.Equal(1_270_062.29, valuation.PresentValue);
        Assert.Equal(3.78, valuation.ParRatePercent);
        Assert.Equal(3.5, valuation.FixedRatePercent);
        Assert.Equal(46_075.02, risk.ParallelDv01);
        Assert.Equal([291.54, 658.72, 1414.48, 43_710.40, 0, 0, 0, 0], risk.Buckets.Select(bucket => bucket.Delta));
        Assert.Equal([1, 2, 3, 5, 7, 10, 20, 30], risk.Buckets.Select(bucket => bucket.TenorYears));
    }

    [Fact]
    public void T1001_annuity_and_par_deltas_match_the_independent_derivation()
    {
        var market = DemoData.Market.ToCurveMarket();
        var swap = UsdSofrOis.Swap(DemoData.AsOf, new SwapTerms(100_000_000, 0.035, PayFixed: true, Tenor.Years(5)));

        var deltas = RiskCalculator.ParDeltas(swap, market).Select(delta => Math.Round(delta.Delta, 2)).ToList();

        Assert.Equal(4.5359367, swap.Annuity(market.Calibrate().Curve), precision: 7);
        Assert.Equal([-23.78, -46.70, -108.09, 45_167.87], deltas.Take(4));
        Assert.All(deltas.Skip(4), later => Assert.Equal(0.0, later, precision: 2));
    }

    [Fact]
    public async Task The_curve_reports_tenor_years_zero_rates_and_discount_factors_per_pillar()
    {
        var curve = await Engine.GetCurveAsync("usd-sofr", Ct);

        Assert.Equal(("USD-SOFR", "2026-09-30", "LogLinearDiscount"), (curve.CurveId, curve.AsOf, curve.Interpolation));
        Assert.Equal(new CurvePillar(1, 4.0237, 0.960138), curve.Pillars[0]);
        Assert.Equal(new CurvePillar(10, 4.0238, 0.668356), curve.Pillars[5]);
    }

    [Fact]
    public async Task Short_tenors_are_reported_as_fractions_of_a_year()
    {
        var market = new MarketSnapshot(
            "SHORT",
            DemoData.AsOf,
            [new ParQuote(Tenor.Parse("3D"), 4.30), new ParQuote(Tenor.Parse("1W"), 4.30), new ParQuote(Tenor.Months(6), 4.20), new ParQuote(Tenor.Years(1), 4.05)],
            InterpolationScheme.LinearZero);

        var curve = await new AnalyticsRiskEngine(market, []).GetCurveAsync("SHORT", Ct);

        Assert.Equal([3 / 365.0, 7 / 365.0, 0.5, 1.0], curve.Pillars.Select(pillar => pillar.TenorYears));
        Assert.Equal("LinearZero", curve.Interpolation);
    }
}
