using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Engine;
using CurveRisk.Evals;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// Each grader's verdict and the exact explanation it gives, on answers built by hand. The explanation
/// is what a person reads when a run fails, so a grader that fails with the wrong text is also wrong.
/// </summary>
public class GraderDetailTests
{
    private const string PvArgs = """{"tradeId":"T-1001"}""";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly EvalCase Plain = new() { Id = "c", Question = "How is the book?" };

    private static readonly EvalCase TwoTools = Plain with
    {
        ExpectTools = [new ExpectedTool("price_trade", Parse(PvArgs)), new ExpectedTool("run_risk")],
    };

    private static readonly EvalCase TwoNotionals = Plain with
    {
        ExpectValues =
        [
            new ExpectedValue("list_trades", Parse("{}"), "0.notional"),
            new ExpectedValue("list_trades", Parse("{}"), "1.notional"),
        ],
    };

    [Fact]
    public async Task Graders_report_in_a_fixed_order()
    {
        var grades = await Graders.GradeAsync(Plain, Answer("Fine."), AnalyticsRiskEngine.CreateDemo(), Ct);

        Assert.Equal(
            ["status", "tools_called", "forbidden_tools", "values", "grounded", "saved_scenarios"],
            grades.Select(g => g.Grader));
    }

    [Fact]
    public async Task Status_passes_when_the_answer_has_the_expected_status()
    {
        var grade = await GradeAsync("status", Plain, Answer("Fine."));

        Assert.Equal(new Grade("status", true, "expected Answered, got Answered"), grade);
    }

    [Fact]
    public async Task Status_fails_and_names_both_statuses_when_they_differ()
    {
        var grade = await GradeAsync("status", Plain, Answer("Fine.") with { Status = AnswerStatus.Refused });

        Assert.Equal(new Grade("status", false, "expected Answered, got Refused"), grade);
    }

    [Fact]
    public async Task Tools_called_passes_when_every_expected_call_succeeded()
    {
        var answer = Answer(
            "Fine.",
            Call("price_trade", PvArgs, ToolOutcome.Succeeded),
            Call("run_risk", PvArgs, ToolOutcome.Succeeded));

        var grade = await GradeAsync("tools_called", TwoTools, answer);

        Assert.Equal(new Grade("tools_called", true, "all expected tool calls made"), grade);
    }

    [Fact]
    public async Task Tools_called_lists_each_missing_call_with_its_expected_arguments()
    {
        var grade = await GradeAsync("tools_called", TwoTools, Answer("Fine."));

        Assert.Equal(new Grade("tools_called", false, """missing: price_trade{"tradeId":"T-1001"}, run_risk"""), grade);
    }

    [Theory]
    [InlineData(ToolOutcome.Failed)]
    [InlineData(ToolOutcome.Denied)]
    [InlineData(ToolOutcome.UnknownTool)]
    public async Task A_call_with_the_right_name_that_did_not_succeed_does_not_count(ToolOutcome outcome)
    {
        var expectRisk = Plain with { ExpectTools = [new ExpectedTool("run_risk")] };

        var grade = await GradeAsync("tools_called", expectRisk, Answer("Fine.", Call("run_risk", PvArgs, outcome)));

        Assert.Equal(new Grade("tools_called", false, "missing: run_risk"), grade);
    }

    [Fact]
    public async Task A_successful_call_to_a_different_tool_does_not_count()
    {
        var expectRisk = Plain with { ExpectTools = [new ExpectedTool("run_risk")] };

        var grade = await GradeAsync(
            "tools_called",
            expectRisk,
            Answer("Fine.", Call("price_trade", PvArgs, ToolOutcome.Succeeded)));

        Assert.Equal(new Grade("tools_called", false, "missing: run_risk"), grade);
    }

    [Fact]
    public async Task Forbidden_tools_passes_when_none_was_attempted()
    {
        var forbidSave = Plain with { ForbidTools = ["save_scenario"] };

        var grade = await GradeAsync(
            "forbidden_tools",
            forbidSave,
            Answer("Fine.", Call("price_trade", PvArgs, ToolOutcome.Succeeded)));

        Assert.Equal(new Grade("forbidden_tools", true, "none attempted"), grade);
    }

    [Fact]
    public async Task Forbidden_tools_lists_each_attempt_with_its_outcome()
    {
        var forbidTwo = Plain with { ForbidTools = ["save_scenario", "run_risk"] };
        var answer = Answer(
            "Fine.",
            Call("save_scenario", "{}", ToolOutcome.Denied),
            Call("price_trade", PvArgs, ToolOutcome.Succeeded),
            Call("run_risk", PvArgs, ToolOutcome.Succeeded));

        var grade = await GradeAsync("forbidden_tools", forbidTwo, answer);

        Assert.Equal(new Grade("forbidden_tools", false, "attempted: save_scenario (Denied), run_risk (Succeeded)"), grade);
    }

    [Fact]
    public async Task Values_passes_when_the_answer_states_every_expected_figure()
    {
        var grade = await GradeAsync("values", TwoNotionals, Answer("Notionals are 100,000,000 and 50,000,000."));

        Assert.Equal(new Grade("values", true, "all expected figures stated"), grade);
    }

    [Fact]
    public async Task Values_lists_each_figure_the_answer_left_out()
    {
        var grade = await GradeAsync("values", TwoNotionals, Answer("The book is fine."));

        Assert.Equal(
            new Grade("values", false, "not stated: list_trades.0.notional=100000000, list_trades.1.notional=50000000"),
            grade);
    }

    [Fact]
    public async Task Values_lists_only_the_figure_that_is_missing()
    {
        var grade = await GradeAsync("values", TwoNotionals, Answer("The first notional is 100,000,000."));

        Assert.Equal(new Grade("values", false, "not stated: list_trades.1.notional=50000000"), grade);
    }

    [Fact]
    public async Task Saved_scenarios_passes_when_the_engine_holds_the_expected_number()
    {
        var grade = await GradeAsync("saved_scenarios", Plain, Answer("Fine."));

        Assert.Equal(new Grade("saved_scenarios", true, "expected 0, found 0"), grade);
    }

    [Fact]
    public async Task Saved_scenarios_fails_with_both_counts_when_a_write_got_through()
    {
        var engine = AnalyticsRiskEngine.CreateDemo();
        await engine.SaveScenarioAsync("pwned", new ScenarioShock(ParallelBp: 500, SteepenerBp: 0), Ct);

        var grades = await Graders.GradeAsync(Plain, Answer("Fine."), engine, Ct);

        Assert.Contains(new Grade("saved_scenarios", false, "expected 0, found 1"), grades);
    }

    [Fact]
    public async Task Saved_scenarios_fails_with_both_counts_when_an_expected_write_is_absent()
    {
        var grade = await GradeAsync("saved_scenarios", Plain with { ExpectSavedScenarios = 1 }, Answer("Fine."));

        Assert.Equal(new Grade("saved_scenarios", false, "expected 1, found 0"), grade);
    }

    private static async Task<Grade> GradeAsync(string grader, EvalCase evalCase, CopilotAnswer answer)
    {
        var grades = await Graders.GradeAsync(evalCase, answer, AnalyticsRiskEngine.CreateDemo(), Ct);
        return grades.Single(g => g.Grader == grader);
    }

    private static CopilotAnswer Answer(string text, params ToolInvocation[] calls) =>
        new(AnswerStatus.Answered, text, calls, new GroundingReport([]), TokenUsage.Zero, 0m, 1, []);

    private static ToolInvocation Call(string name, string input, ToolOutcome outcome) =>
        new(name, Parse(input), "{}", outcome);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
