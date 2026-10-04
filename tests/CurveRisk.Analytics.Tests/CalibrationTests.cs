using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Time;
using CurveRisk.Engine;

namespace CurveRisk.Analytics.Tests;

public class CalibrationTests
{
    private static readonly DateTime AsOf = DemoData.AsOf;

    public static TheoryData<InterpolationScheme> Schemes =>
        [InterpolationScheme.LogLinearDiscount, InterpolationScheme.LinearZero, InterpolationScheme.MonotoneCubicLogDiscount];

    private static CurveMarket Market(InterpolationScheme scheme) => (DemoData.Market with { Scheme = scheme }).ToCurveMarket();

    [Theory]
    [MemberData(nameof(Schemes))]
    public void A_calibrated_curve_reprices_every_input_quote(InterpolationScheme scheme)
    {
        var market = Market(scheme);

        var result = market.Calibrate();

        Assert.Equal(market.Instruments.Count, result.Residuals.Count);
        Assert.All(result.Residuals, residual => Assert.InRange(residual, -CurveBootstrapper.Tolerance, CurveBootstrapper.Tolerance));
        Assert.All(market.Instruments, quote => Assert.Equal(quote.Quote, quote.ModelQuote(result.Curve), precision: 11));
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Calibrated_discount_factors_decrease_with_maturity(InterpolationScheme scheme)
    {
        var factors = Market(scheme).Calibrate().Curve.DiscountFactors;

        Assert.All(factors.Zip(factors.Skip(1), (earlier, later) => later < earlier), Assert.True);
        Assert.InRange(factors[0], 0.95, 0.97);
    }

    [Fact]
    public void A_local_scheme_needs_one_sweep_and_a_look_ahead_scheme_needs_more()
    {
        Assert.Equal(1, Market(InterpolationScheme.LogLinearDiscount).Calibrate().Sweeps);
        Assert.Equal(1, Market(InterpolationScheme.LinearZero).Calibrate().Sweeps);
        Assert.True(Market(InterpolationScheme.MonotoneCubicLogDiscount).Calibrate().Sweeps > 1);
    }

    [Fact]
    public void A_single_deposit_gives_the_closed_form_discount_factor()
    {
        var end = AsOf.AddDays(90);
        var deposit = new Deposit(AsOf, end, 0.04, DayCount.Act360);
        var market = new CurveMarket(AsOf, [deposit], InterpolationScheme.LogLinearDiscount);

        var curve = market.Calibrate().Curve;

        Assert.Equal(1.0 / (1.0 + (0.04 * 90 / 360)), curve.DiscountFactor(end), precision: 13);
        Assert.Equal("Deposit 2026-12-29", deposit.Label);
        Assert.Equal(end, deposit.PillarDate);
    }

    [Fact]
    public void Instruments_may_be_supplied_in_any_order()
    {
        var market = Market(InterpolationScheme.LogLinearDiscount);
        var reversed = market with { Instruments = [.. market.Instruments.Reverse()] };

        Assert.Equal(market.Calibrate().Curve.DiscountFactors, reversed.Calibrate().Curve.DiscountFactors);
    }

    [Fact]
    public void Bumping_a_quote_changes_only_that_quote()
    {
        var market = Market(InterpolationScheme.LogLinearDiscount);

        var bumped = market.BumpQuote(3, 0.0001);

        Assert.Equal(market.Instruments[3].Quote + 0.0001, bumped.Instruments[3].Quote, precision: 15);
        Assert.Equal(market.Instruments[2].Quote, bumped.Instruments[2].Quote);
        Assert.Equal("OIS 5Y", bumped.Instruments[3].Label);
    }

    [Fact]
    public void An_empty_market_and_duplicate_pillars_are_rejected()
    {
        var scheme = InterpolationScheme.LogLinearDiscount;
        var quote = UsdSofrOis.Quote(AsOf, Tenor.Years(2), 0.04);

        Assert.Throws<CalibrationException>(() => new CurveMarket(AsOf, [], scheme).Calibrate());
        var duplicate = Assert.Throws<CalibrationException>(() => new CurveMarket(AsOf, [quote, quote], scheme).Calibrate());
        Assert.Contains("share a pillar date", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quote_no_discount_factor_can_reproduce_names_the_instrument()
    {
        var impossible = UsdSofrOis.Quote(AsOf, Tenor.Years(1), -0.9);

        var ex = Assert.Throws<CalibrationException>(
            () => new CurveMarket(AsOf, [impossible], InterpolationScheme.LogLinearDiscount).Calibrate());

        Assert.Contains("OIS 1Y", ex.Message, StringComparison.Ordinal);
        Assert.IsType<Numerics.RootFindingException>(ex.InnerException);
    }

    [Fact]
    public void A_market_that_cannot_converge_fails_loudly()
    {
        var never = new NeverRepricing(AsOf.AddDays(365));

        var ex = Assert.Throws<CalibrationException>(
            () => new CurveMarket(AsOf, [never], InterpolationScheme.LogLinearDiscount).Calibrate());

        Assert.Contains("did not converge", ex.Message, StringComparison.Ordinal);
        Assert.Contains("largest quote error", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void Forward_rates_on_a_calibrated_curve_are_positive_between_and_beyond_the_pillars(InterpolationScheme scheme)
    {
        var curve = Market(scheme).Calibrate().Curve;

        var forwards = Enumerable.Range(0, 140)
            .Select(quarter => curve.ForwardRate(AsOf.AddDays(quarter * 91), AsOf.AddDays((quarter + 1) * 91), DayCount.Act360));

        Assert.All(forwards, forward => Assert.InRange(forward, 0.02, 0.07));
    }

    [Fact]
    public void A_deposit_that_started_before_the_curve_date_is_refused_instead_of_misquoted()
    {
        var seasoned = new Deposit(AsOf.AddDays(-30), AsOf.AddDays(60), 0.04, DayCount.Act360);
        var curve = Market(InterpolationScheme.LogLinearDiscount).Calibrate().Curve;

        Assert.Throws<ArgumentException>(() => seasoned.ModelQuote(curve));
        Assert.Throws<ArgumentException>(() => curve.ForwardRate(AsOf.AddDays(10), AsOf.AddDays(10), DayCount.Act360));
        Assert.Throws<ArgumentException>(() => curve.ForwardRate(AsOf.AddDays(10), AsOf.AddDays(5), DayCount.Act360));
    }

    /// <summary>
    /// Its model quote drifts a little on every evaluation, so each pillar solve finds a root, yet the
    /// check afterwards never sees the same function and never passes.
    /// </summary>
    private sealed record NeverRepricing(DateTime PillarDate) : ICalibrationInstrument
    {
        private int _calls;

        public string Label => "Never";

        public double Quote => 0.0;

        public double ModelQuote(DiscountCurve curve) =>
            curve.DiscountFactor(PillarDate) - 0.5 - (1e-9 * Interlocked.Increment(ref _calls));

        public ICalibrationInstrument WithQuote(double quote) => this;
    }
}
