using CurveRisk.Workflows;

namespace CurveRisk.Copilot.Briefs;

/// <summary>
/// Runs the risk brief graph over one engine. Where <see cref="CopilotAgent"/> lets the model choose
/// each step, this follows a fixed procedure and uses the model for one paragraph, so the same request
/// always takes the same path and its figures never pass through the model.
/// </summary>
/// <param name="model">Null when no provider is configured; the brief is then produced without commentary.</param>
/// <param name="tools">Over the engine to report on. Its approval gate decides whether a proposed save runs.</param>
public sealed class RiskBriefWorkflow(IModelClient? model, ToolExecutor tools, CopilotOptions? options = null, BudgetGuard? budget = null)
{
    private readonly GraphRunner<RiskBriefState> _runner = new(
        RiskBriefGraph.Create(model, tools, options ?? new CopilotOptions(), budget ?? BudgetGuard.Unlimited),
        RiskBriefGraph.MaxSteps(options ?? new CopilotOptions()));

    /// <summary>The graph's structure, for diagrams. It is the definition that runs, with nothing to run it against.</summary>
    public static GraphShape Shape { get; } = RiskBriefGraph
        .Create(model: null, new ToolExecutor([], new DenyAllApprovalGate()), new CopilotOptions(), BudgetGuard.Unlimited)
        .Shape;

    /// <param name="offerSave">Pause after the brief to ask whether its worst scenario should be saved.</param>
    public Task<GraphRun<RiskBriefState>> RunAsync(bool offerSave, CancellationToken cancellationToken) =>
        _runner.RunAsync(new RiskBriefState { OfferSave = offerSave }, cancellationToken);

    /// <summary>Continues a brief that paused for approval. Only the save step runs; nothing is recomputed.</summary>
    public Task<GraphRun<RiskBriefState>> ResumeAsync(RiskBriefState paused, CancellationToken cancellationToken) =>
        _runner.ResumeAsync(RiskBriefGraph.AwaitApproval, paused, cancellationToken);
}
