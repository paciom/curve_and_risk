using System.Text.Json;
using CurveRisk.Copilot;
using CurveRisk.Evals;

namespace CurveRisk.Ai.Tests;

/// <summary>The report's arithmetic and its markdown, on results built by hand so every figure is a literal.</summary>
public class EvalReportTests
{
    private static readonly Grade ValuesPass = new("values", true, "all expected figures stated");
    private static readonly Grade StatusPass = new("status", true, "expected Answered, got Answered");
    private static readonly Grade StatusFail = new("status", false, "expected Answered, got Refused");

    [Fact]
    public void A_report_with_failures_renders_the_table_in_grader_order_and_a_failures_section()
    {
        var report = new EvalReport("m", "high", 2,
        [
            Trial("a", 1, 0.0100m, ValuesPass, StatusPass),
            Trial("a", 2, 0.0250m, new Grade("values", false, "not stated: x"), StatusFail),
            Trial("b", 1, 0.0001m, new Grade("values", false, "not stated: y"), StatusPass),
        ]);

        var lines = report.ToMarkdown().Split(Environment.NewLine);

        Assert.Equal(
            [
                "# Copilot eval report",
                "",
                "Model `m`, effort `high`, 2 trial(s) per case, 3 runs.",
                "",
                "**Pass rate: 33.3 %** (1/3). Estimated cost: $0.0351.",
                "",
                "| Grader | Pass rate |",
                "|---|---|",
                "| status | 66.7 % |",
                "| values | 33.3 % |",
                "",
                "## Failures",
                "",
                "- `a` (trial 2): values: not stated: x | status: expected Answered, got Refused",
                "- `b` (trial 1): values: not stated: y",
                "",
            ],
            lines);
    }

    [Fact]
    public void A_report_without_failures_has_no_failures_section()
    {
        var report = new EvalReport("m", "default", 1, [Trial("a", 1, 0.5m, ValuesPass, StatusPass)]);

        var lines = report.ToMarkdown().Split(Environment.NewLine);

        Assert.Equal(
            [
                "# Copilot eval report",
                "",
                "Model `m`, effort `default`, 1 trial(s) per case, 1 runs.",
                "",
                "**Pass rate: 100.0 %** (1/1). Estimated cost: $0.5000.",
                "",
                "| Grader | Pass rate |",
                "|---|---|",
                "| status | 100.0 % |",
                "| values | 100.0 % |",
                "",
            ],
            lines);
    }

    [Fact]
    public void A_report_with_no_results_has_a_pass_rate_of_zero_and_no_cost()
    {
        var report = new EvalReport("m", "default", 1, []);

        Assert.Equal(0.0, report.PassRate);
        Assert.Equal(0m, report.TotalCostUsd);
        Assert.Empty(report.PassRateByGrader);
        Assert.Contains("**Pass rate: 0.0 %** (0/0). Estimated cost: $0.0000.", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public void Total_cost_is_the_sum_over_every_run()
    {
        var report = new EvalReport("m", "default", 1,
        [
            Trial("a", 1, 0.25m, StatusPass),
            Trial("b", 1, 1.50m, StatusPass),
            Trial("c", 1, 0.75m, StatusPass),
        ]);

        Assert.Equal(2.50m, report.TotalCostUsd);
    }

    [Fact]
    public void Pass_rate_is_the_share_of_runs_in_which_every_grader_passed()
    {
        var report = new EvalReport("m", "default", 1,
        [
            Trial("a", 1, 0m, StatusPass, ValuesPass),
            Trial("b", 1, 0m, StatusFail, ValuesPass),
            Trial("c", 1, 0m, StatusFail, ValuesPass),
            Trial("d", 1, 0m, StatusPass, ValuesPass),
        ]);

        Assert.Equal(0.5, report.PassRate);
        Assert.Equal(0.5, report.PassRateByGrader["status"]);
        Assert.Equal(1.0, report.PassRateByGrader["values"]);
    }

    [Fact]
    public void Only_failed_runs_carrying_one_of_the_tags_are_safety_failures()
    {
        var report = new EvalReport("m", "default", 1,
        [
            Trial("passed-injection", 1, 0m, StatusPass) with { Tags = ["injection"] },
            Trial("failed-injection", 1, 0m, StatusFail) with { Tags = ["Injection"] },
            Trial("failed-pricing", 1, 0m, StatusFail) with { Tags = ["pricing"] },
        ]);

        var failures = report.FailuresTagged(["injection", "writes"]);

        Assert.Equal(["failed-injection"], failures.Select(r => r.CaseId));
    }

    [Fact]
    public void A_trial_result_records_each_tool_call_with_its_arguments_and_outcome()
    {
        var answer = Answer(0m) with
        {
            ToolCalls =
            [
                new ToolInvocation("price_trade", JsonSerializer.SerializeToElement(new { tradeId = "T-1001" }), "{}", ToolOutcome.Succeeded),
                new ToolInvocation("save_scenario", JsonSerializer.SerializeToElement(new { name = "x" }), "no", ToolOutcome.Denied),
            ],
        };

        var result = TrialResult.From(new EvalCase { Id = "a", Question = "q" }, 1, answer, [StatusPass]);

        Assert.Equal(
            ["""price_trade{"tradeId":"T-1001"} -> Succeeded""", """save_scenario{"name":"x"} -> Denied"""],
            result.ToolCalls);
    }

    [Fact]
    public void A_trial_passes_only_when_every_grader_passed()
    {
        var evalCase = new EvalCase { Id = "a", Question = "q" };

        var allPassed = TrialResult.From(evalCase, 1, Answer(0m), [StatusPass, ValuesPass]);
        var oneFailed = TrialResult.From(evalCase, 1, Answer(0m), [StatusPass, new Grade("values", false, "not stated: x")]);

        Assert.True(allPassed.Passed);
        Assert.False(oneFailed.Passed);
    }

    private static TrialResult Trial(string caseId, int trial, decimal costUsd, params Grade[] grades) =>
        TrialResult.From(new EvalCase { Id = caseId, Question = "q" }, trial, Answer(costUsd), grades);

    private static CopilotAnswer Answer(decimal costUsd) =>
        new(AnswerStatus.Answered, "text", [], new GroundingReport([]), TokenUsage.Zero, costUsd, 1, []);
}
