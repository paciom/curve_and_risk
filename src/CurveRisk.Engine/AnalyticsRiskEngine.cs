using System.Collections.Concurrent;
using System.Globalization;
using CurveRisk.Ai.Tools;
using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Risk;
using CurveRisk.Analytics.Time;

namespace CurveRisk.Engine;

/// <summary>
/// The pricing and risk engine behind the AI tools: calibrates a curve from a market snapshot with
/// CurveRisk.Analytics and values booked swaps on it. Rates cross this boundary in percent and
/// shocks in basis points, as the tool contracts say; inside the library they are fractions.
/// </summary>
public sealed class AnalyticsRiskEngine : IRiskEngine
{
    private const string Currency = "USD";
    private const double MaxShockBp = 1000;
    private const double FractionToPercent = 100.0;

    private readonly MarketSnapshot _market;
    private readonly IReadOnlyList<TradeDefinition> _trades;
    private readonly Lazy<DiscountCurve> _curve;
    private readonly ConcurrentDictionary<string, SavedScenario> _scenarios = new();
    private int _nextScenario;

    public AnalyticsRiskEngine(MarketSnapshot market, IReadOnlyList<TradeDefinition> trades)
    {
        _market = market;
        _trades = trades;
        _curve = new Lazy<DiscountCurve>(() => market.ToCurveMarket().Calibrate().Curve);
    }

    public IReadOnlyCollection<SavedScenario> SavedScenarios => [.. _scenarios.Values];

    /// <summary>An engine over the illustrative market and book in <see cref="DemoData"/>.</summary>
    public static AnalyticsRiskEngine CreateDemo() => new(DemoData.Market, DemoData.Trades);

    public Task<IReadOnlyList<TradeSummary>> ListTradesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TradeSummary>>([.. _trades.Select(trade => new TradeSummary(
            trade.TradeId,
            "InterestRateSwap",
            trade.Terms.PayFixed ? "PayFixed" : "ReceiveFixed",
            trade.Terms.Notional,
            Math.Round(trade.Terms.FixedRate * FractionToPercent, 4),
            trade.Terms.Tenor.Count,
            _market.CurveId,
            trade.Description))]);

    public Task<CurveSnapshot> GetCurveAsync(string curveId, CancellationToken cancellationToken)
    {
        if (!string.Equals(curveId, _market.CurveId, StringComparison.OrdinalIgnoreCase))
        {
            throw new RiskEngineException($"Unknown curve '{curveId}'. Available curves: {_market.CurveId}.");
        }

        var curve = _curve.Value;
        var pillars = OrderedQuotes().Select((quote, pillar) => new CurvePillar(
            YearsOf(quote.Tenor),
            Math.Round(curve.ZeroRate(curve.PillarTimes[pillar]) * FractionToPercent, 4),
            Math.Round(curve.DiscountFactors[pillar], 6)));

        return Task.FromResult(new CurveSnapshot(
            _market.CurveId,
            _market.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _market.Scheme.ToString(),
            [.. pillars]));
    }

    public Task<TradeValuation> PriceTradeAsync(string tradeId, CancellationToken cancellationToken)
    {
        var (trade, swap) = Find(tradeId);
        var curve = _curve.Value;
        return Task.FromResult(new TradeValuation(
            trade.TradeId,
            Currency,
            Math.Round(swap.PresentValue(curve), 2),
            Math.Round(swap.ParRate(curve) * FractionToPercent, 4),
            Math.Round(trade.Terms.FixedRate * FractionToPercent, 4)));
    }

    public Task<RiskReport> RunRiskAsync(string tradeId, CancellationToken cancellationToken)
    {
        var (trade, swap) = Find(tradeId);
        var curve = _curve.Value;
        var buckets = RiskCalculator.ZeroDeltas(swap, curve)
            .Zip(OrderedQuotes(), (sensitivity, quote) => new BucketDelta(YearsOf(quote.Tenor), Math.Round(sensitivity.Delta, 2)));

        return Task.FromResult(new RiskReport(
            trade.TradeId,
            Currency,
            Math.Round(RiskCalculator.ParallelDv01(swap, curve), 2),
            [.. buckets]));
    }

    public Task<ScenarioResult> RunScenarioAsync(string tradeId, ScenarioShock shock, CancellationToken cancellationToken)
    {
        var (trade, swap) = Find(tradeId);
        Validate(shock);

        var curve = _curve.Value;
        var basePv = Math.Round(swap.PresentValue(curve), 2);
        var shockedPv = Math.Round(swap.PresentValue(new CurveShock(shock.ParallelBp, shock.SteepenerBp).Apply(curve)), 2);
        return Task.FromResult(new ScenarioResult(trade.TradeId, Currency, shock, basePv, shockedPv, Math.Round(shockedPv - basePv, 2)));
    }

    public Task<SavedScenario> SaveScenarioAsync(string name, ScenarioShock shock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RiskEngineException("Scenario name must not be empty.");
        }

        Validate(shock);
        var saved = new SavedScenario($"S-{Interlocked.Increment(ref _nextScenario):D4}", name.Trim(), shock);
        _scenarios[saved.ScenarioId] = saved;
        return Task.FromResult(saved);
    }

    private static double YearsOf(Tenor tenor) => tenor.Unit switch
    {
        TenorUnit.Years => tenor.Count,
        TenorUnit.Months => tenor.Count / 12.0,
        TenorUnit.Weeks => tenor.Count * 7 / 365.0,
        _ => tenor.Count / 365.0,
    };

    private static void Validate(ScenarioShock shock)
    {
        var magnitudes = new[] { Math.Abs(shock.ParallelBp), Math.Abs(shock.SteepenerBp) };
        if (magnitudes.Any(magnitude => !double.IsFinite(magnitude) || magnitude > MaxShockBp))
        {
            throw new RiskEngineException("Shocks must be finite and within +/-1000bp.");
        }
    }

    /// <summary>Quotes in the same order as the curve's pillars.</summary>
    private IEnumerable<ParQuote> OrderedQuotes() => _market.Quotes.OrderBy(quote => quote.Tenor.AddTo(_market.AsOf));

    private (TradeDefinition Trade, InterestRateSwap Swap) Find(string tradeId)
    {
        var trade = _trades.FirstOrDefault(candidate => string.Equals(candidate.TradeId, tradeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new RiskEngineException(
                $"Unknown trade '{tradeId}'. Known trades: {string.Join(", ", _trades.Select(candidate => candidate.TradeId))}.");

        return (trade, UsdSofrOis.Swap(_market.AsOf, trade.Terms));
    }
}
