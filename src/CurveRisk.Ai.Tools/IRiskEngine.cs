namespace CurveRisk.Ai.Tools;

/// <summary>
/// The port the AI layer uses to reach pricing and risk. Every number an agent reports
/// originates from an implementation of this interface, never from the model.
/// </summary>
public interface IRiskEngine
{
    Task<IReadOnlyList<TradeSummary>> ListTradesAsync(CancellationToken cancellationToken);

    Task<CurveSnapshot> GetCurveAsync(string curveId, CancellationToken cancellationToken);

    Task<TradeValuation> PriceTradeAsync(string tradeId, CancellationToken cancellationToken);

    Task<RiskReport> RunRiskAsync(string tradeId, CancellationToken cancellationToken);

    Task<ScenarioResult> RunScenarioAsync(string tradeId, ScenarioShock shock, CancellationToken cancellationToken);

    Task<SavedScenario> SaveScenarioAsync(string name, ScenarioShock shock, CancellationToken cancellationToken);
}

/// <summary>Raised for caller mistakes (unknown ids, invalid inputs). The message is safe to show to a model.</summary>
public sealed class RiskEngineException(string message) : Exception(message);

public sealed record TradeSummary(
    string TradeId,
    string Product,
    string Direction,
    double Notional,
    double FixedRatePercent,
    int MaturityYears,
    string CurveId,
    string Description);

public sealed record CurvePillar(double TenorYears, double ZeroRatePercent, double DiscountFactor);

public sealed record CurveSnapshot(string CurveId, string AsOf, string Interpolation, IReadOnlyList<CurvePillar> Pillars);

public sealed record TradeValuation(string TradeId, string Currency, double PresentValue, double ParRatePercent, double FixedRatePercent);

public sealed record BucketDelta(double TenorYears, double Delta);

/// <param name="ParallelDv01">
/// PV change for a +1bp parallel shift of continuously compounded zero rates. This is zero-rate risk,
/// not par-quote risk: the two differ by a few percent and a hedge should be sized from par deltas.
/// </param>
/// <param name="Buckets">PV change for a +1bp bump of each pillar's zero rate in isolation.</param>
public sealed record RiskReport(string TradeId, string Currency, double ParallelDv01, IReadOnlyList<BucketDelta> Buckets);

/// <param name="ParallelBp">Shift applied to every zero rate, in basis points.</param>
/// <param name="SteepenerBp">
/// Change in the 2s10s zero spread, in basis points: -half at 2Y and below, +half at 10Y and above, linear between.
/// </param>
public sealed record ScenarioShock(double ParallelBp, double SteepenerBp);

public sealed record ScenarioResult(
    string TradeId,
    string Currency,
    ScenarioShock Shock,
    double BasePresentValue,
    double ShockedPresentValue,
    double ProfitAndLoss);

public sealed record SavedScenario(string ScenarioId, string Name, ScenarioShock Shock);
