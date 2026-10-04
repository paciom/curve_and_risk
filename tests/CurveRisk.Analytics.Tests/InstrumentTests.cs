using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Time;
using CurveRisk.Engine;

namespace CurveRisk.Analytics.Tests;

public class InstrumentTests
{
    private const double FlatRate = 0.04;

    private static readonly DateTime AsOf = DemoData.AsOf;
    private static readonly DiscountCurve Calibrated = DemoData.Market.ToCurveMarket().Calibrate().Curve;

    /// <summary>A flat continuously compounded curve, so every discount factor is exp(-r t) and PVs have closed forms.</summary>
    private static readonly DiscountCurve Flat = new DiscountCurve(
        AsOf, [AsOf.AddDays(3650)], [1.0], InterpolationScheme.LogLinearDiscount).ShiftZeroRates(_ => FlatRate);

    private static double Df(DateTime date) => Math.Exp(-FlatRate * (date - AsOf).TotalDays / 365.0);

    private static InterestRateSwap Swap(double fixedRate, bool payFixed, double notional = 100_000_000) =>
        UsdSofrOis.Swap(AsOf, new SwapTerms(notional, fixedRate, payFixed, Tenor.Years(5)));

    [Fact]
    public void Usd_sofr_swaps_start_two_business_days_out_and_pay_annually_on_act_360()
    {
        var swap = Swap(0.035, payFixed: true);

        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Unspecified), swap.Effective);
        Assert.Equal(new DateTime(2031, 10, 2, 0, 0, 0, DateTimeKind.Unspecified), swap.Maturity);
        Assert.Equal(5, swap.FixedLeg.Count);
        Assert.Equal(365.0 / 360, swap.FixedLeg[2].YearFraction, precision: 14);
        Assert.Equal(swap.Effective, UsdSofrOis.SpotDate(AsOf));
        Assert.Equal("US", UsdSofrOis.Calendar.Name);
    }

    [Fact]
    public void A_swap_on_a_flat_curve_matches_the_hand_calculation()
    {
        var swap = Swap(0.035, payFixed: true);

        var annuity = swap.FixedLeg.Sum(period => period.YearFraction * Df(period.PaymentDate));
        var floating = Df(swap.Effective) - Df(swap.Maturity);

        Assert.Equal(annuity, swap.Annuity(Flat), precision: 13);
        Assert.Equal(floating / annuity, swap.ParRate(Flat), precision: 13);
        Assert.Equal(100_000_000 * (floating - (0.035 * annuity)), swap.PresentValue(Flat), precision: 5);
    }

    [Fact]
    public void A_swap_struck_at_its_par_rate_is_worth_nothing()
    {
        var par = Swap(0.035, payFixed: true).ParRate(Calibrated);

        Assert.Equal(0.0, Swap(par, payFixed: true).PresentValue(Calibrated), precision: 6);
        Assert.Equal(0.0378, par, precision: 11);
    }

    [Fact]
    public void A_swap_that_has_already_started_is_refused_instead_of_valued_as_if_no_coupon_had_been_paid()
    {
        var seasoned = UsdSofrOis.Swap(AsOf.AddYears(-2), new SwapTerms(100_000_000, 0.035, PayFixed: true, Tenor.Years(5)));

        var ex = Assert.Throws<NotSupportedException>(() => seasoned.PresentValue(Calibrated));

        Assert.Contains("Seasoned swaps are not supported", ex.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => seasoned.ParRate(Calibrated));
        Assert.Throws<NotSupportedException>(() => seasoned.Annuity(Calibrated));
    }

    [Theory]
    [InlineData(0.030)]
    [InlineData(0.045)]
    public void Payer_and_receiver_are_opposite_and_pv_scales_with_notional(double fixedRate)
    {
        var payer = Swap(fixedRate, payFixed: true);
        var receiver = Swap(fixedRate, payFixed: false);

        Assert.Equal(-payer.PresentValue(Calibrated), receiver.PresentValue(Calibrated), precision: 6);
        Assert.Equal(2.5 * payer.PresentValue(Calibrated), Swap(fixedRate, true, 250_000_000).PresentValue(Calibrated), precision: 4);
        Assert.Equal(Math.Sign(payer.ParRate(Calibrated) - fixedRate), Math.Sign(payer.PresentValue(Calibrated)));
    }

    [Fact]
    public void An_ois_quote_is_the_par_rate_of_its_own_swap_and_can_be_requoted()
    {
        var quote = UsdSofrOis.Quote(AsOf, Tenor.Years(5), 0.0378);

        Assert.Equal(quote.Swap.ParRate(Calibrated), quote.ModelQuote(Calibrated));
        Assert.Equal(quote.Swap.Maturity, quote.PillarDate);
        Assert.Equal(0.04, quote.WithQuote(0.04).Quote);
        Assert.Equal("OIS 5Y", quote.Label);
    }

    [Fact]
    public void A_forward_rate_agreement_is_worth_nothing_at_the_forward_and_has_the_right_sign_away_from_it()
    {
        var period = new Deposit(AsOf.AddDays(90), AsOf.AddDays(180), 0.0, DayCount.Act360);
        var forward = Flat.ForwardRate(period.Start, period.End, DayCount.Act360);

        var atMarket = new ForwardRateAgreement(10_000_000, forward, PayFixed: true, period);
        var bought = new ForwardRateAgreement(10_000_000, forward - 0.01, PayFixed: true, period);
        var sold = bought with { PayFixed = false };

        Assert.Equal(forward, atMarket.ForwardRate(Flat));
        Assert.Equal(0.0, atMarket.PresentValue(Flat), precision: 8);
        Assert.Equal(10_000_000 * 0.25 * 0.01 * Df(period.End), bought.PresentValue(Flat), precision: 6);
        Assert.Equal(-bought.PresentValue(Flat), sold.PresentValue(Flat), precision: 8);

        var started = atMarket with { Period = period with { Start = AsOf.AddDays(-1) } };
        Assert.Throws<ArgumentException>(() => started.PresentValue(Flat));
        Assert.Equal(0.05, period.WithQuote(0.05).Quote);
    }

    [Fact]
    public void A_bond_is_the_sum_of_its_discounted_coupons_and_principal()
    {
        var bond = Bond(AsOf, years: 3);

        var expected = bond.Coupons.Sum(c => 1_000_000 * 0.05 * c.YearFraction * Df(c.PaymentDate)) + (1_000_000 * Df(bond.Maturity));

        Assert.Equal(expected, bond.PresentValue(Flat), precision: 6);
        Assert.Equal(0.0, bond.AccruedInterest(AsOf));
        Assert.Equal(expected / 1_000_000 * 100, bond.CleanPrice(Flat), precision: 9);
        Assert.True(bond.CleanPrice(Flat) > 100, "a 5% coupon is above the 4% curve, so the bond trades above par");
    }

    [Fact]
    public void A_seasoned_bond_excludes_the_coupon_already_paid_and_accrues_the_current_one_on_its_own_day_count()
    {
        // Issued 18 months ago: the first annual coupon was paid six months ago, the second is accruing.
        var bond = Bond(AsOf.AddMonths(-18), years: 2);
        var current = bond.Coupons[1];
        Assert.True(bond.Coupons[0].PaymentDate < AsOf);

        var remaining = (1_000_000 * 0.05 * current.YearFraction * Df(current.PaymentDate)) + (1_000_000 * Df(bond.Maturity));

        Assert.Equal(remaining, bond.PresentValue(Flat), precision: 6);

        // 30/360: exactly six months elapsed is half a coupon, whatever the calendar day count says.
        Assert.Equal(1_000_000 * 0.05 * 0.5, bond.AccruedInterest(AsOf), precision: 8);
        Assert.Equal((remaining - 25_000) / 10_000, bond.CleanPrice(Flat), precision: 9);
    }

    [Fact]
    public void A_matured_bond_is_worth_nothing_and_accrues_nothing()
    {
        var bond = Bond(AsOf.AddYears(-3), years: 2);

        Assert.Equal(0.0, bond.PresentValue(Flat));
        Assert.Equal(0.0, bond.AccruedInterest(AsOf));
    }

    private static FixedRateBond Bond(DateTime issue, int years)
    {
        var spec = new ScheduleSpec(
            issue, issue.AddYears(years), 12, DayCount.Thirty360, BusinessCalendar.WeekendsOnly, BusinessDayConvention.Unadjusted);
        return new FixedRateBond(1_000_000, 0.05, DayCount.Thirty360, Schedule.Build(spec));
    }
}
