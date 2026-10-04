using CurveRisk.Ai.Tools;
using CurveRisk.Ai.Tools.Fixtures;
using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

internal sealed class ThrowingOnRiskEngine : IRiskEngine
{
    private readonly FixtureRiskEngine _inner = new();

    public Task<IReadOnlyList<TradeSummary>> ListTradesAsync(CancellationToken cancellationToken) => _inner.ListTradesAsync(cancellationToken);

    public Task<CurveSnapshot> GetCurveAsync(string curveId, CancellationToken cancellationToken) => _inner.GetCurveAsync(curveId, cancellationToken);

    public Task<TradeValuation> PriceTradeAsync(string tradeId, CancellationToken cancellationToken) => _inner.PriceTradeAsync(tradeId, cancellationToken);

    public Task<RiskReport> RunRiskAsync(string tradeId, CancellationToken cancellationToken) => throw new TimeoutException("secret connection string");

    public Task<ScenarioResult> RunScenarioAsync(string tradeId, ScenarioShock shock, CancellationToken cancellationToken) => _inner.RunScenarioAsync(tradeId, shock, cancellationToken);

    public Task<SavedScenario> SaveScenarioAsync(string name, ScenarioShock shock, CancellationToken cancellationToken) => _inner.SaveScenarioAsync(name, shock, cancellationToken);
}

internal sealed class RecordingGate(ApprovalDecision decision) : IApprovalGate
{
    public List<ToolCallPart> Asked { get; } = [];

    public Task<ApprovalDecision> RequestAsync(ToolCallPart toolCall, CancellationToken cancellationToken)
    {
        Asked.Add(toolCall);
        return Task.FromResult(decision);
    }
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
