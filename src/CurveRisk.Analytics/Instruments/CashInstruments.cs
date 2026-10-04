using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Time;

namespace CurveRisk.Analytics.Instruments;

/// <summary>
/// A simply compounded deposit: one unit lent at <see cref="Start"/>, repaid with interest at <see cref="End"/>.
/// </summary>
/// <param name="Quote">The deposit rate, as a fraction.</param>
public sealed record Deposit(DateTime Start, DateTime End, double Quote, DayCount DayCount) : ICalibrationInstrument
{
    public string Label => $"Deposit {End:yyyy-MM-dd}";

    public DateTime PillarDate => End;

    public double ModelQuote(DiscountCurve curve) => curve.ForwardRate(Start, End, DayCount);

    public ICalibrationInstrument WithQuote(double quote) => this with { Quote = quote };
}

/// <summary>
/// Forward rate agreement, settled at the end of the period (no discounting to the start date).
/// </summary>
/// <param name="Strike">The agreed rate, as a fraction.</param>
/// <param name="PayFixed">True when we pay the strike and receive the floating rate (a bought FRA).</param>
public sealed record ForwardRateAgreement(double Notional, double Strike, bool PayFixed, Deposit Period) : IPriceable
{
    public double ForwardRate(DiscountCurve curve) => Period.ModelQuote(curve);

    public double PresentValue(DiscountCurve curve)
    {
        var accrual = Period.DayCount.YearFraction(Period.Start, Period.End);
        var bought = Notional * accrual * (ForwardRate(curve) - Strike) * curve.DiscountFactor(Period.End);
        return PayFixed ? bought : -bought;
    }
}

/// <summary>Bullet bond paying a fixed coupon. PV is of the cash flows still to come, from the holder's side.</summary>
/// <param name="FaceValue">Principal repaid at maturity.</param>
/// <param name="CouponRate">Annual coupon, as a fraction of face value.</param>
/// <param name="Coupons">Coupon periods in date order.</param>
/// <param name="DayCount">The coupon day count, used for accrued interest. Must match the one the periods were built with.</param>
public sealed record FixedRateBond(double FaceValue, double CouponRate, DayCount DayCount, IReadOnlyList<Period> Coupons) : IPriceable
{
    public DateTime Maturity => Coupons[Coupons.Count - 1].AccrualEnd;

    /// <summary>Dirty value: discounted coupons and principal payable after the curve's as-of date.</summary>
    public double PresentValue(DiscountCurve curve)
    {
        var coupons = Coupons
            .Where(period => period.PaymentDate > curve.AsOf)
            .Sum(period => FaceValue * CouponRate * period.YearFraction * curve.DiscountFactor(period.PaymentDate));

        var principal = Maturity > curve.AsOf ? FaceValue * curve.DiscountFactor(Maturity) : 0.0;
        return coupons + principal;
    }

    /// <summary>Coupon earned but not yet paid at <paramref name="settlement"/>, under the bond's own day count.</summary>
    public double AccruedInterest(DateTime settlement)
    {
        var current = Coupons.FirstOrDefault(period => period.AccrualStart <= settlement.Date && settlement.Date < period.AccrualEnd);
        return current is null
            ? 0.0
            : FaceValue * CouponRate * DayCount.YearFraction(current.AccrualStart, settlement.Date);
    }

    /// <summary>Quoted price per 100 of face value: dirty value less accrued interest at the as-of date.</summary>
    public double CleanPrice(DiscountCurve curve) =>
        (PresentValue(curve) - AccruedInterest(curve.AsOf)) / FaceValue * 100.0;
}
