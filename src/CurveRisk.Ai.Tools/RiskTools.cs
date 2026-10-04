using System.ComponentModel;

namespace CurveRisk.Ai.Tools;

/// <summary>
/// The tool surface offered to models. Descriptions here are prompt text: they are what the model reads
/// to decide which tool to call, so they say when to use the tool and what the units are.
/// </summary>
public sealed class RiskTools(IRiskEngine engine)
{
    [Description("List the trades in the portfolio with their terms. Call this first when the user refers to a trade without giving its id.")]
    public Task<IReadOnlyList<TradeSummary>> ListTrades(CancellationToken cancellationToken) =>
        engine.ListTradesAsync(cancellationToken);

    [Description("Get a calibrated curve: zero rates (percent, continuously compounded) and discount factors at each pillar tenor.")]
    public Task<CurveSnapshot> GetCurve(
        [Description("Curve identifier, for example USD-SOFR.")] string curveId,
        CancellationToken cancellationToken) =>
        engine.GetCurveAsync(curveId, cancellationToken);

    [Description("Price one trade off its curve. Returns present value in trade currency and the par rate in percent.")]
    public Task<TradeValuation> PriceTrade(
        [Description("Trade identifier, for example T-1001.")] string tradeId,
        CancellationToken cancellationToken) =>
        engine.PriceTradeAsync(tradeId, cancellationToken);

    [Description("Compute interest-rate risk for one trade: parallel DV01 and per-pillar bucketed deltas, each the PV change in trade currency for a +1bp bump of zero rates (zero-rate risk, not par-quote risk).")]
    public Task<RiskReport> RunRisk(
        [Description("Trade identifier, for example T-1001.")] string tradeId,
        CancellationToken cancellationToken) =>
        engine.RunRiskAsync(tradeId, cancellationToken);

    [Description("Reprice one trade under a curve shock and return base PV, shocked PV and P&L. Use this for any what-if question; never estimate P&L from DV01 yourself.")]
    public Task<ScenarioResult> RunScenario(
        [Description("Trade identifier, for example T-1001.")] string tradeId,
        [Description("Parallel shift of all zero rates in basis points. Positive means rates up.")] double parallelBp,
        [Description("Change in the 2s10s spread in basis points. Positive steepens: 2Y moves by minus half, 10Y by plus half.")] double steepenerBp,
        CancellationToken cancellationToken) =>
        engine.RunScenarioAsync(tradeId, new ScenarioShock(parallelBp, steepenerBp), cancellationToken);

    [Description("Persist a named scenario so it can be reused. This changes stored data and needs the user's approval.")]
    public Task<SavedScenario> SaveScenario(
        [Description("Human-readable scenario name.")] string name,
        [Description("Parallel shift in basis points.")] double parallelBp,
        [Description("2s10s steepener in basis points.")] double steepenerBp,
        CancellationToken cancellationToken) =>
        engine.SaveScenarioAsync(name, new ScenarioShock(parallelBp, steepenerBp), cancellationToken);
}
