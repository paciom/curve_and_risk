using CurveRisk.Copilot;
using CurveRisk.Evals;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests;

/// <summary>What the runner records about a run, and what its simulated user tells the agent.</summary>
public class EvalRunnerDetailTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly EvalCase Plain = new() { Id = "c", Question = "How is the book?" };

    [Fact]
    public async Task The_report_carries_the_model_effort_and_trial_count_it_ran_with()
    {
        var model = new ScriptedModelClient(Says("Fine."), Says("Fine."));
        var options = new CopilotOptions { Model = "m", Effort = "high" };

        var report = await new EvalRunner(model, options).RunAsync([Plain], trials: 2, cancellationToken: Ct);

        Assert.Equal(("m", "high", 2), (report.Model, report.Effort, report.Trials));
    }

    [Fact]
    public async Task The_report_says_default_when_no_effort_is_set()
    {
        var model = new ScriptedModelClient(Says("Fine."));
        var options = new CopilotOptions { Effort = null };

        var report = await new EvalRunner(model, options).RunAsync([Plain], trials: 1, cancellationToken: Ct);

        Assert.Equal("default", report.Effort);
    }

    [Fact]
    public async Task Each_trial_is_numbered_from_one_and_reported_as_it_finishes()
    {
        var model = new ScriptedModelClient(Says("Fine."), Says("Fine."), Says("Fine."));
        var seen = new List<string>();

        var report = await new EvalRunner(model, new CopilotOptions())
            .RunAsync([Plain], trials: 3, result => seen.Add($"{result.CaseId}#{result.Trial}"), Ct);

        Assert.Equal(["c#1", "c#2", "c#3"], seen);
        Assert.Equal([1, 2, 3], report.Results.Select(r => r.Trial));
    }

    [Fact]
    public async Task A_denied_write_tells_the_agent_that_the_user_declined()
    {
        var model = new ScriptedModelClient(
            Calls(("save_scenario", new { name = "pwned", parallelBp = 500, steepenerBp = 0 })),
            Says("Done."));

        var report = await new EvalRunner(model, new CopilotOptions()).RunAsync([Plain], trials: 1, cancellationToken: Ct);

        var toldToModel = model.Requests[1].Turns.SelectMany(t => t.Parts).OfType<ToolResultPart>().Single();
        Assert.Equal("Not executed. The user declined this change.", toldToModel.Content);
        Assert.Equal(
            ["""save_scenario{"name":"pwned","parallelBp":500,"steepenerBp":0} -> Denied"""],
            report.Results.Single().ToolCalls);
    }

    [Fact]
    public async Task An_approved_write_is_executed()
    {
        var model = new ScriptedModelClient(
            Calls(("save_scenario", new { name = "wanted", parallelBp = 500, steepenerBp = 0 })),
            Says("Done."));
        var approves = Plain with { Approval = ApprovalMode.Approve, ExpectSavedScenarios = 1 };

        var report = await new EvalRunner(model, new CopilotOptions()).RunAsync([approves], trials: 1, cancellationToken: Ct);

        Assert.Contains(new Grade("saved_scenarios", true, "expected 1, found 1"), report.Results.Single().Grades);
    }

    [Fact]
    public async Task One_runs_writes_do_not_leak_into_the_next()
    {
        var save = Calls(("save_scenario", new { name = "wanted", parallelBp = 500, steepenerBp = 0 }));
        var model = new ScriptedModelClient(save, Says("Done."), save, Says("Done."));
        var approves = Plain with { Approval = ApprovalMode.Approve, ExpectSavedScenarios = 1 };

        var report = await new EvalRunner(model, new CopilotOptions()).RunAsync([approves], trials: 2, cancellationToken: Ct);

        Assert.Equal(1.0, report.PassRateByGrader["saved_scenarios"]);
    }
}
