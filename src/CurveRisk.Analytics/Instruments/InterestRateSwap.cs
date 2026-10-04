using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Time;

namespace CurveRisk.Analytics.Instruments;

/// <summary>
/// Fixed-for-floating swap valued on a single curve: the floating leg is projected and discounted on
/// the same curve, so with no spread it is worth notional x (DF(start) - DF(end)).
/// </summary>
/// <param name="Notional">In currency units; positive.</param>
/// <param name="FixedRate">As a fraction: 0.035 for 3.50%.</param>
/// <param name="PayFixed">True when we pay the fixed rate and receive floating.</param>
/// <param name="FixedLeg">Fixed-leg accrual periods, in date order.</param>
public sealed record InterestRateSwap(double Notional, double FixedRate, bool PayFixed, IReadOnlyList<Period> FixedLeg) : IPriceable
{
    public DateTime Effective => FixedLeg[0].AccrualStart;

    public DateTime Maturity => FixedLeg[FixedLeg.Count - 1].AccrualEnd;

    /// <summary>PV of one unit of fixed rate per unit notional: the sum of accrual fraction x discount factor.</summary>
    public double Annuity(DiscountCurve curve)
    {
        RequireNotStarted(curve);
        return FixedLeg.Sum(period => period.YearFraction * curve.DiscountFactor(period.PaymentDate));
    }

    /// <summary>The fixed rate at which the swap is worth zero, as a fraction.</summary>
    public double ParRate(DiscountCurve curve) =>
        (curve.DiscountFactor(Effective) - curve.DiscountFactor(Maturity)) / Annuity(curve);

    /// <summary>From our side. A payer gains when the par rate is above the fixed rate.</summary>
    public double PresentValue(DiscountCurve curve)
    {
        var receiveFloating = Notional * (ParRate(curve) - FixedRate) * Annuity(curve);
        return PayFixed ? receiveFloating : -receiveFloating;
    }

    // A swap already under way needs its paid coupons dropped and the current floating period valued
    // from fixings, neither of which this model has. Valuing it as if unstarted would be wrong by
    // every coupon already paid, so it is refused.
    private void RequireNotStarted(DiscountCurve curve)
    {
        if (Effective < curve.AsOf)
        {
            throw new NotSupportedException(
                $"The swap started on {Effective:yyyy-MM-dd}, before the curve date {curve.AsOf:yyyy-MM-dd}. Seasoned swaps are not supported.");
        }
    }
}

/// <summary>The economic terms of a swap, separate from the market conventions that lay out its schedule.</summary>
public sealed record SwapTerms(double Notional, double FixedRate, bool PayFixed, Tenor Tenor);

/// <summary>
/// USD SOFR overnight index swap conventions: spot start two business days after trade date, annual
/// fixed payments, ACT/360, modified following, US calendar. Payment lag is not modelled.
/// </summary>
public static class UsdSofrOis
{
    private const int SpotLagDays = 2;
    private const int AnnualMonths = 12;

    public static BusinessCalendar Calendar => BusinessCalendar.UnitedStates;

    public static DateTime SpotDate(DateTime tradeDate) => Calendar.AddBusinessDays(tradeDate, SpotLagDays);

    public static InterestRateSwap Swap(DateTime tradeDate, SwapTerms terms)
    {
        var effective = SpotDate(tradeDate);
        var spec = new ScheduleSpec(
            effective,
            terms.Tenor.AddTo(effective),
            AnnualMonths,
            DayCount.Act360,
            Calendar,
            BusinessDayConvention.ModifiedFollowing);

        return new InterestRateSwap(terms.Notional, terms.FixedRate, terms.PayFixed, Schedule.Build(spec));
    }

    /// <summary>A market par quote for the given tenor, ready for calibration.</summary>
    public static OisQuote Quote(DateTime tradeDate, Tenor tenor, double parRate) =>
        new(tenor, parRate, Swap(tradeDate, new SwapTerms(1.0, parRate, PayFixed: true, tenor)));
}

/// <summary>A par OIS rate as a calibration instrument.</summary>
public sealed record OisQuote(Tenor Tenor, double Quote, InterestRateSwap Swap) : ICalibrationInstrument
{
    public string Label => $"OIS {Tenor}";

    public DateTime PillarDate => Swap.Maturity;

    public double ModelQuote(DiscountCurve curve) => Swap.ParRate(curve);

    public ICalibrationInstrument WithQuote(double quote) => this with { Quote = quote };
}
