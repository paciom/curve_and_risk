using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Risk;
using CurveRisk.Analytics.Time;
using CurveRisk.Engine;

namespace CurveRisk.Analytics.Tests;

public class RiskTests
{
    private static readonly DateTime AsOf = DemoData.AsOf;

    public static TheoryData<InterpolationScheme> AdditiveSchemes =>
        [InterpolationScheme.LogLinearDiscount, InterpolationScheme.LinearZero];

    private static CurveMarket Market(InterpolationScheme scheme = InterpolationScheme.LogLinearDiscount) =>
        (DemoData.Market with { Scheme = scheme }).ToCurveMarket();

    private static InterestRateSwap Swap(int years, bool payFixed, double fixedRate = 0.035) =>
        UsdSofrOis.Swap(AsOf, new SwapTerms(100_000_000, fixedRate, payFixed, Tenor.Years(years)));

    [Fact]
    public void A_payer_gains_and_a_receiver_loses_when_rates_rise()
    {
        var curve = Market().Calibrate().Curve;

        var payer = RiskCalculator.ParallelDv01(Swap(5, payFixed: true), curve);
        var receiver = RiskCalculator.ParallelDv01(Swap(5, payFixed: false), curve);

        Assert.True(payer > 0);
        Assert.Equal(-payer, receiver, precision: 6);
    }

    [Fact]
    public void Dv01_matches_the_analytic_derivative_of_pv_with_respect_to_a_parallel_zero_shift()
    {
        var curve = Market().Calibrate().Curve;
        var swap = Swap(5, payFixed: true);

        // A discount factor moves by minus its time times itself per unit of zero rate, so each cash
        // flow of the swap contributes its time-weighted value: the float leg's two notional flows
        // with opposite signs, and the fixed coupons.
        double Weighted(DateTime date) => curve.TimeTo(date) * curve.DiscountFactor(date);
        var analytic = 100_000_000 * 1e-4
            * (-Weighted(swap.Effective) + Weighted(swap.Maturity) + (0.035 * swap.FixedLeg.Sum(p => p.YearFraction * Weighted(p.PaymentDate))));

        var dv01 = RiskCalculator.ParallelDv01(swap, curve);

        // The one-sided bump carries a little convexity; 0.1% is well above it and far below any real error.
        Assert.Equal(analytic, dv01, tolerance: Math.Abs(analytic) * 1e-3);
    }

    [Theory]
    [MemberData(nameof(AdditiveSchemes))]
    public void Zero_rate_buckets_add_up_to_the_parallel_dv01(InterpolationScheme scheme)
    {
        var curve = Market(scheme).Calibrate().Curve;
        var swap = Swap(7, payFixed: true);

        var buckets = RiskCalculator.ZeroDeltas(swap, curve);
        var parallel = RiskCalculator.ParallelDv01(swap, curve);

        Assert.Equal(curve.PillarDates.Count, buckets.Count);
        Assert.Equal(parallel, buckets.Sum(bucket => bucket.Delta), tolerance: Math.Abs(parallel) * 1e-4);
    }

    [Fact]
    public void Zero_rate_risk_sits_at_and_before_maturity_and_nowhere_after()
    {
        var curve = Market().Calibrate().Curve;

        var buckets = RiskCalculator.ZeroDeltas(Swap(5, payFixed: true), curve);

        var fiveYear = buckets[3];
        Assert.Equal("2031-10-02", fiveYear.Label);
        Assert.Equal(curve.PillarTimes[3], fiveYear.TimeYears);
        Assert.Same(fiveYear, buckets.MaxBy(bucket => Math.Abs(bucket.Delta)));
        Assert.All(buckets.Skip(4), later => Assert.Equal(0.0, later.Delta, precision: 6));
    }

    [Fact]
    public void A_par_swap_shows_its_whole_par_risk_in_its_own_maturity_bucket()
    {
        var market = Market();
        var fiveYearPar = market.Instruments[3].Quote;

        var deltas = RiskCalculator.ParDeltas(Swap(5, payFixed: true, fiveYearPar), market);

        var annuityRisk = 100_000_000 * UsdSofrOis.Swap(AsOf, new SwapTerms(1, fiveYearPar, true, Tenor.Years(5))).Annuity(market.Calibrate().Curve) * 1e-4;
        Assert.Equal("OIS 5Y", deltas[3].Label);
        Assert.Equal(annuityRisk, deltas[3].Delta, tolerance: annuityRisk * 1e-3);
        Assert.All(deltas.Where((_, i) => i != 3), other => Assert.InRange(other.Delta, -1.0, 1.0));
    }

    [Fact]
    public void Par_risk_of_an_off_market_swap_still_concentrates_at_maturity_and_sums_near_the_dv01()
    {
        var market = Market();
        var curve = market.Calibrate().Curve;
        var swap = Swap(10, payFixed: false, fixedRate: 0.0425);

        var deltas = RiskCalculator.ParDeltas(swap, market);

        Assert.Same(deltas[5], deltas.MaxBy(delta => Math.Abs(delta.Delta)));
        Assert.True(deltas[5].Delta < 0, "a receiver loses when the 10Y rate rises");
        // Zero-rate DV01 and total par risk measure different bumps and differ by a few percent, no more.
        Assert.InRange(deltas.Sum(delta => delta.Delta) / RiskCalculator.ParallelDv01(swap, curve), 0.95, 1.0);
    }

    [Theory]
    [InlineData(10, 0, 1.0, 0.0010)]
    [InlineData(10, 0, 25.0, 0.0010)]
    [InlineData(0, 40, 1.0, -0.0020)]
    [InlineData(0, 40, 2.0, -0.0020)]
    [InlineData(0, 40, 6.0, 0.0)]
    [InlineData(0, 40, 10.0, 0.0020)]
    [InlineData(0, 40, 30.0, 0.0020)]
    [InlineData(-25, 40, 8.0, -0.0015)]
    public void A_shock_shifts_zero_rates_by_the_parallel_amount_plus_the_twist(double parallelBp, double steepenerBp, double years, double expected)
    {
        Assert.Equal(expected, new CurveShock(parallelBp, steepenerBp).ShiftAt(years), precision: 15);
    }

    [Fact]
    public void A_zero_shock_changes_nothing_and_a_small_shock_agrees_with_dv01()
    {
        var curve = Market().Calibrate().Curve;
        var swap = Swap(10, payFixed: true);
        var basePv = swap.PresentValue(curve);

        var flat = swap.PresentValue(new CurveShock(0, 0).Apply(curve)) - basePv;
        var upTen = swap.PresentValue(new CurveShock(10, 0).Apply(curve)) - basePv;
        var dv01 = RiskCalculator.ParallelDv01(swap, curve);

        Assert.Equal(0.0, flat);
        Assert.Equal(10 * dv01, upTen, tolerance: Math.Abs(10 * dv01) * 0.01);
        Assert.True(upTen < 10 * dv01, "a payer swap is negatively convex in this parametrisation: gains grow slower than linearly");
    }
}
