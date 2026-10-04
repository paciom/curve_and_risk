using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Numerics;

namespace CurveRisk.Analytics.Curves;

/// <summary>Raised when a curve cannot be made to reprice its instruments. The message says which and by how much.</summary>
public sealed class CalibrationException(string message, Exception? inner = null) : Exception(message, inner);

/// <param name="Residuals">Model quote minus market quote per instrument, as fractions. All within tolerance.</param>
/// <param name="Sweeps">Passes over the pillars. One for local interpolation schemes; more when a scheme looks ahead.</param>
public sealed record CalibrationResult(DiscountCurve Curve, IReadOnlyList<double> Residuals, int Sweeps);

/// <summary>The inputs that define a curve: quotes as of a date, and how to interpolate between them.</summary>
public sealed record CurveMarket(DateTime AsOf, IReadOnlyList<ICalibrationInstrument> Instruments, InterpolationScheme Scheme)
{
    public CalibrationResult Calibrate() => CurveBootstrapper.Calibrate(this);

    /// <summary>The same market with one instrument's quote moved by <paramref name="shift"/> (a fraction).</summary>
    public CurveMarket BumpQuote(int instrument, double shift)
    {
        var bumped = Instruments.ToArray();
        bumped[instrument] = bumped[instrument].WithQuote(bumped[instrument].Quote + shift);
        return this with { Instruments = bumped };
    }
}

/// <summary>
/// Iterative bootstrap. Each pass solves the pillars in maturity order, one discount factor at a time,
/// with a bracketing root find. With a local interpolation scheme one pass is exact. A scheme whose
/// value before a pillar depends on later pillars (the monotone cubic) needs further passes, and the
/// loop repeats until every instrument reprices.
/// </summary>
public static class CurveBootstrapper
{
    /// <summary>Maximum absolute difference between model and market quote, as a fraction (1e-12 is 1e-8 bp).</summary>
    public const double Tolerance = 1e-12;

    private const int MaxSweeps = 50;
    private const double LowestDiscountFactor = 1e-8;
    private const double HighestDiscountFactor = 2.0;
    private const double InitialZeroRate = 0.03;

    public static CalibrationResult Calibrate(CurveMarket market)
    {
        var instruments = market.Instruments.OrderBy(instrument => instrument.PillarDate).ToList();
        var curve = InitialCurve(market, instruments);

        var worst = double.NaN;
        for (var sweep = 1; sweep <= MaxSweeps; sweep++)
        {
            curve = Sweep(curve, instruments);
            var residuals = instruments.Select(instrument => instrument.ModelQuote(curve) - instrument.Quote).ToList();
            worst = residuals.Max(residual => Math.Abs(residual));
            if (worst <= Tolerance)
            {
                return new CalibrationResult(curve, residuals, sweep);
            }
        }

        throw new CalibrationException(
            $"Calibration did not converge in {MaxSweeps} sweeps; the largest quote error is {worst:E3} against a tolerance of {Tolerance:E0}.");
    }

    private static DiscountCurve InitialCurve(CurveMarket market, List<ICalibrationInstrument> instruments)
    {
        if (instruments.Count == 0)
        {
            throw new CalibrationException("A curve needs at least one calibration instrument.");
        }

        var dates = instruments.Select(instrument => instrument.PillarDate).ToList();
        if (dates.Distinct().Count() != dates.Count)
        {
            throw new CalibrationException("Two calibration instruments share a pillar date; the curve would be over-determined.");
        }

        var seed = new DiscountCurve(market.AsOf, dates, dates.Select(_ => 1.0).ToList(), market.Scheme);
        return seed.ShiftZeroRates(_ => InitialZeroRate);
    }

    private static DiscountCurve Sweep(DiscountCurve curve, List<ICalibrationInstrument> instruments)
    {
        for (var pillar = 0; pillar < instruments.Count; pillar++)
        {
            curve = curve.WithDiscountFactor(pillar, SolvePillar(curve, instruments[pillar], pillar));
        }

        return curve;
    }

    private static double SolvePillar(DiscountCurve curve, ICalibrationInstrument instrument, int pillar)
    {
        try
        {
            return Brent.Solve(
                factor => instrument.ModelQuote(curve.WithDiscountFactor(pillar, factor)) - instrument.Quote,
                LowestDiscountFactor,
                HighestDiscountFactor);
        }
        catch (RootFindingException ex)
        {
            throw new CalibrationException($"Cannot calibrate {instrument.Label} (quote {instrument.Quote}): {ex.Message}", ex);
        }
    }
}
