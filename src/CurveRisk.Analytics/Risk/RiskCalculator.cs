using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;

namespace CurveRisk.Analytics.Risk;

/// <summary>PV change for a +1bp move at one point of the curve.</summary>
/// <param name="Label">The pillar date, or the calibration instrument for par risk.</param>
/// <param name="TimeYears">Curve time of the pillar.</param>
/// <param name="Delta">PV change in currency units, from our side.</param>
public sealed record PillarSensitivity(string Label, double TimeYears, double Delta);

/// <summary>
/// First-order interest-rate risk by bump and revalue. Every figure is the PV change for a move of
/// +1 basis point, from our side: positive means we gain when rates rise.
/// </summary>
public static class RiskCalculator
{
    public const double OneBasisPoint = 1e-4;

    /// <summary>PV change when every zero rate rises by 1bp.</summary>
    public static double ParallelDv01(IPriceable trade, DiscountCurve curve) =>
        trade.PresentValue(curve.ShiftZeroRates(_ => OneBasisPoint)) - trade.PresentValue(curve);

    /// <summary>
    /// Zero-rate risk: each pillar's zero rate bumped on its own. Shows where on the curve the risk
    /// sits. With log-linear or linear-zero interpolation the buckets add up to the parallel DV01.
    /// </summary>
    public static IReadOnlyList<PillarSensitivity> ZeroDeltas(IPriceable trade, DiscountCurve curve)
    {
        var basePv = trade.PresentValue(curve);
        var times = curve.PillarTimes;
        return curve.PillarDates
            .Select((date, pillar) => new PillarSensitivity(
                date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                times[pillar],
                trade.PresentValue(curve.BumpPillar(pillar, OneBasisPoint)) - basePv))
            .ToList();
    }

    /// <summary>
    /// Par risk: each market quote bumped on its own and the whole curve recalibrated. This is the
    /// risk in terms of the instruments one would hedge with; a par swap shows its whole risk in
    /// its own maturity's bucket.
    /// </summary>
    public static IReadOnlyList<PillarSensitivity> ParDeltas(IPriceable trade, CurveMarket market)
    {
        var baseCurve = market.Calibrate().Curve;
        var basePv = trade.PresentValue(baseCurve);
        return market.Instruments
            .Select((instrument, index) => new PillarSensitivity(
                instrument.Label,
                baseCurve.TimeTo(instrument.PillarDate),
                trade.PresentValue(market.BumpQuote(index, OneBasisPoint).Calibrate().Curve) - basePv))
            .ToList();
    }
}

/// <summary>A curve scenario, in basis points.</summary>
/// <param name="ParallelBp">Added to every zero rate. Positive means rates up.</param>
/// <param name="SteepenerBp">
/// Change in the 2s10s zero spread: minus half at two years and below, plus half at ten years and
/// above, linear in between. Positive steepens.
/// </param>
public sealed record CurveShock(double ParallelBp, double SteepenerBp)
{
    private const double ShortPivotYears = 2.0;
    private const double LongPivotYears = 10.0;

    /// <summary>The zero-rate shift at <paramref name="timeYears"/>, as a fraction.</summary>
    public double ShiftAt(double timeYears)
    {
        var position = (timeYears - ShortPivotYears) / (LongPivotYears - ShortPivotYears);
        var twist = Math.Min(Math.Max(position, 0.0), 1.0) - 0.5;
        return (ParallelBp + (SteepenerBp * twist)) * RiskCalculator.OneBasisPoint;
    }

    public DiscountCurve Apply(DiscountCurve curve) => curve.ShiftZeroRates(ShiftAt);
}
