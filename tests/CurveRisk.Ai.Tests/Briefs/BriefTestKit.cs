using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Copilot.Briefs;
using CurveRisk.Engine;

namespace CurveRisk.Ai.Tests.Briefs;

internal static class Brief
{
    public static readonly string[] EnginePath = ["load_portfolio", "price_trades", "run_risk", "run_scenarios", "gather", "find_flags"];

    public static RiskBriefWorkflow Workflow(IModelClient? model, IRiskEngine? engine = null, IApprovalGate? gate = null) =>
        new(model, Tools(engine ?? AnalyticsRiskEngine.CreateDemo(), gate));

    public static ToolExecutor Tools(IRiskEngine engine, IApprovalGate? gate = null) =>
        new(ToolCatalog.Create(engine), gate ?? new DenyAllApprovalGate());

    /// <summary>A model response written from the facts the graph sent, as a real model's would be.</summary>
    public static Func<ModelRequest, ModelResponse> SaysFromFacts(Func<JsonElement, string> render) => request =>
    {
        using var facts = JsonDocument.Parse(FactsJsonIn(request));
        return ScriptedModelClient.Says(render(facts.RootElement))(request);
    };

    public static string FactsJsonIn(ModelRequest request)
    {
        var text = ((TextPart)request.Turns[0].Parts[0]).Text;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        return text[start..(text.LastIndexOf('}') + 1)];
    }

    public static string Money(JsonElement element, string property) =>
        element.GetProperty(property).GetDouble().ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One trade with every figure fixed by the test, so totals can be checked by hand.</summary>
/// <param name="ScenarioPnl">P&amp;L for ParallelUp, ParallelDown, Steepener, Flattener, in that order.</param>
internal sealed record StubTrade(
    string TradeId,
    double PresentValue,
    double ParallelDv01,
    IReadOnlyList<BucketDelta> Buckets,
    IReadOnlyList<double> ScenarioPnl,
    string Currency = "USD",
    string Description = "");

/// <summary>An engine that returns exactly what it was given: no pricing, so expected figures are arithmetic a reader can do.</summary>
internal sealed class StubEngine(params StubTrade[] trades) : IRiskEngine
{
    private static readonly ScenarioShock[] Shocks = [new(50, 0), new(-50, 0), new(0, 50), new(0, -50)];

    public List<SavedScenario> Saved { get; } = [];

    public bool FailListing { get; init; }

    public bool FailSaving { get; init; }

    /// <summary>When set, every valuation and risk report comes back labelled with this id, as an engine that matched the wrong trade would.</summary>
    public string? AnswerAs { get; init; }

    /// <summary>Called before each valuation, with how many there have been.</summary>
    public Action<int>? OnPrice { get; init; }

    public int Priced { get; private set; }

    public Task<IReadOnlyList<TradeSummary>> ListTradesAsync(CancellationToken cancellationToken) => FailListing
        ? throw new RiskEngineException("The book could not be read.")
        : Task.FromResult<IReadOnlyList<TradeSummary>>(
            [.. trades.Select(t => new TradeSummary(t.TradeId, "InterestRateSwap", "PayFixed", 1_000_000, 3.5, 5, "USD-SOFR", t.Description))]);

    public Task<CurveSnapshot> GetCurveAsync(string curveId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<TradeValuation> PriceTradeAsync(string tradeId, CancellationToken cancellationToken)
    {
        OnPrice?.Invoke(Priced++);
        return Task.FromResult(new TradeValuation(AnswerAs ?? tradeId, Find(tradeId).Currency, Find(tradeId).PresentValue, 3.75, 3.5));
    }

    public Task<RiskReport> RunRiskAsync(string tradeId, CancellationToken cancellationToken) =>
        Task.FromResult(new RiskReport(AnswerAs ?? tradeId, Find(tradeId).Currency, Find(tradeId).ParallelDv01, Find(tradeId).Buckets));

    public Task<ScenarioResult> RunScenarioAsync(string tradeId, ScenarioShock shock, CancellationToken cancellationToken)
    {
        var trade = Find(tradeId);
        var pnl = trade.ScenarioPnl[Array.IndexOf(Shocks, shock)];
        return Task.FromResult(new ScenarioResult(tradeId, trade.Currency, shock, trade.PresentValue, trade.PresentValue + pnl, pnl));
    }

    public Task<SavedScenario> SaveScenarioAsync(string name, ScenarioShock shock, CancellationToken cancellationToken)
    {
        if (FailSaving)
        {
            throw new RiskEngineException("A scenario with that name already exists.");
        }

        Saved.Add(new SavedScenario($"S-{Saved.Count + 1}", name, shock));
        return Task.FromResult(Saved[^1]);
    }

    private StubTrade Find(string tradeId) => trades.Single(trade => trade.TradeId == tradeId);
}

internal static class StubBook
{
    public static StubTrade First { get; } = new(
        "A-1", 100.10, -10, [new BucketDelta(2, 1), new BucketDelta(5, -11)], [-500, 480, 20, -25]);

    public static StubTrade Second { get; } = new(
        "A-2", 200.20, 4, [new BucketDelta(2, 3), new BucketDelta(5, 1)], [200, -190, -40, 35]);

    public static StubEngine Engine() => new(First, Second);
}
