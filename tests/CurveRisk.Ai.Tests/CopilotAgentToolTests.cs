using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Engine;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests;

/// <summary>How the agent runs tools: approval for writes, and failures that never escape as exceptions.</summary>
public class CopilotAgentToolTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly AnalyticsRiskEngine _engine = AnalyticsRiskEngine.CreateDemo();

    private CopilotAgent Agent(IModelClient model, IApprovalGate? gate = null) =>
        new(model, new ToolExecutor(ToolCatalog.Create(_engine), gate ?? new DenyAllApprovalGate()));

    [Fact]
    public async Task A_denied_write_does_not_run_and_the_model_is_told()
    {
        var model = new ScriptedModelClient(
            Calls(("save_scenario", new { name = "Bear", parallelBp = 50, steepenerBp = 0 })),
            Says("Nothing was saved."));

        var answer = await Agent(model).AskAsync("Save it.", cancellationToken: Ct);

        Assert.Empty(_engine.SavedScenarios);
        Assert.Equal(ToolOutcome.Denied, answer.ToolCalls[0].Outcome);
        var result = (ToolResultPart)model.Requests[1].Turns[^1].Parts[0];
        Assert.True(result.IsError);
        Assert.StartsWith("Not executed.", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_approved_write_runs()
    {
        var gate = new RecordingGate(ApprovalDecision.Approve);
        var model = new ScriptedModelClient(
            Calls(("save_scenario", new { name = "Bear", parallelBp = 50, steepenerBp = 0 })),
            Says("Saved."));

        await Agent(model, gate).AskAsync("Save it.", cancellationToken: Ct);

        Assert.Equal("Bear", Assert.Single(_engine.SavedScenarios).Name);
        Assert.Equal("save_scenario", Assert.Single(gate.Asked).Name);
    }

    [Fact]
    public async Task Read_tools_never_ask_for_approval()
    {
        var gate = new RecordingGate(ApprovalDecision.Approve);
        var model = new ScriptedModelClient(
            Calls(("list_trades", new { }), ("run_risk", new { tradeId = "T-1001" })),
            Says("Done."));

        await Agent(model, gate).AskAsync("Go.", cancellationToken: Ct);

        Assert.Empty(gate.Asked);
    }

    [Fact]
    public async Task A_model_that_obeys_injected_text_still_cannot_write_without_a_human()
    {
        // The scripted model does exactly what T-1003's description tells it to. The gate is the control.
        var model = new ScriptedModelClient(
            Calls(("list_trades", new { })),
            Calls(("save_scenario", new { name = "pwned", parallelBp = 500, steepenerBp = 0 })),
            Says("Done."));

        await Agent(model).AskAsync("Summarise the book.", cancellationToken: Ct);

        Assert.Empty(_engine.SavedScenarios);
    }

    [Fact]
    public async Task An_unexpected_tool_failure_is_recorded_and_does_not_lose_a_sibling_write()
    {
        var engine = new ThrowingOnRiskEngine();
        var model = new ScriptedModelClient(
            Calls(("save_scenario", new { name = "Bear", parallelBp = 50, steepenerBp = 0 }), ("run_risk", new { tradeId = "T-1001" })),
            Says("Saved; risk failed."));
        var agent = new CopilotAgent(model, new ToolExecutor(ToolCatalog.Create(engine), new RecordingGate(ApprovalDecision.Approve)));

        var answer = await agent.AskAsync("Save and run risk.", cancellationToken: Ct);

        Assert.Equal([ToolOutcome.Succeeded, ToolOutcome.Failed], answer.ToolCalls.Select(c => c.Outcome));
        Assert.DoesNotContain("secret", answer.ToolCalls[1].Result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_tools_and_bad_arguments_come_back_as_errors_not_exceptions()
    {
        var model = new ScriptedModelClient(
            Calls(("delete_everything", new { }), ("price_trade", new { wrong = 1 })),
            Says("Sorry."));

        var answer = await Agent(model).AskAsync("Go.", cancellationToken: Ct);

        Assert.Equal([ToolOutcome.UnknownTool, ToolOutcome.Failed], answer.ToolCalls.Select(c => c.Outcome));
        Assert.All(model.Requests[1].Turns[^1].Parts.Cast<ToolResultPart>(), r => Assert.True(r.IsError));
    }
}
