using CurveRisk.Analytics.Curves;

namespace CurveRisk.Analytics.Instruments;

/// <summary>Something with a present value on a curve. PV is always from our side, in the trade's currency.</summary>
public interface IPriceable
{
    double PresentValue(DiscountCurve curve);
}

/// <summary>
/// A market quote the curve must reproduce. Calibration drives <c>ModelQuote(curve) - Quote</c> to zero
/// by moving the discount factor at <see cref="PillarDate"/>.
/// </summary>
public interface ICalibrationInstrument
{
    /// <summary>Human-readable name for error messages and risk reports, such as "OIS 5Y".</summary>
    string Label { get; }

    /// <summary>The curve date this instrument determines: its last cash flow.</summary>
    DateTime PillarDate { get; }

    /// <summary>The market quote, as a fraction (0.0385 for 3.85%).</summary>
    double Quote { get; }

    /// <summary>The quote the given curve implies for this instrument, as a fraction.</summary>
    double ModelQuote(DiscountCurve curve);

    /// <summary>The same instrument with a different market quote. Used to bump quotes for par risk.</summary>
    ICalibrationInstrument WithQuote(double quote);
}
