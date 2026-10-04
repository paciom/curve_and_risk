using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Engine;
using CurveRisk.Evals;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// Tests of the eval harness itself. A grader that cannot fail is worse than no grader, so each one is
/// shown here to fail on a scripted model that makes the specific mistake it exists to catch.
/// These run without credentials; they say nothing about how the real model scores.
/// </summary>
public class EvalHarnessTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly EvalCase PvCase = new()
    {
        Id = "pv",
        Question = "What is the PV of T-1001?",
        ExpectTools = [new ExpectedTool("price_trade", Json(new { tradeId = "T-1001" }))],
        ExpectValues = [new ExpectedValue("price_trade", Json(new { tradeId = "T-1001" }), "presentValue")],
        ForbidTools = ["save_scenario"],
    };

    private static string DatasetPath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CurveRisk.slnx")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory!.FullName, "evals", "datasets", "copilot.jsonl");
        }
    }

    [Fact]
    public async Task Dataset_is_well_formed_and_every_reference_resolves_against_the_engine()
    {
        var cases = EvalDataset.Load(DatasetPath);
        var catalog = ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()).ToDictionary(t => t.Name);

        Assert.True(cases.Count >= 15);
        foreach (var evalCase in cases)
        {
            Assert.All(evalCase.ExpectTools, t => Assert.Contains(t.Name, catalog.Keys));
            Assert.All(evalCase.ForbidTools, t => Assert.Contains(t, catalog.Keys));

            foreach (var value in evalCase.ExpectValues)
            {
                using var result = JsonDocument.Parse(await ToolCatalog.InvokeAsync(catalog[value.Tool], value.Args, Ct));
                Assert.Equal(JsonValueKind.Number, Graders.Resolve(result.RootElement, value.Path).ValueKind);
            }
        }
    }

    [Fact]
    public async Task A_correct_run_passes_every_grader()
    {
        var result = await RunAsync(
            PvCase,
            Calls(("price_trade", new { tradeId = "T-1001" })),
            SaysFromLastResult(r => $"PV is USD {r.GetProperty("presentValue").GetDouble():N2}."));

        Assert.True(result.Passed, string.Join(" | ", result.Grades.Where(g => !g.Passed).Select(g => g.Detail)));
    }

    [Fact]
    public async Task Answering_without_the_tool_fails_tools_called_and_values()
    {
        var result = await RunAsync(PvCase, Says("I cannot look that up."));

        Assert.Equal(["tools_called", "values"], Failed(result));
    }

    [Fact]
    public async Task Pricing_the_wrong_trade_fails_tools_called()
    {
        var result = await RunAsync(
            PvCase,
            Calls(("price_trade", new { tradeId = "T-1002" })),
            SaysFromLastResult(r => $"PV is USD {r.GetProperty("presentValue").GetDouble():N2}."));

        Assert.Equal(["tools_called", "values"], Failed(result));
    }

    [Fact]
    public async Task Calling_the_tool_but_not_stating_the_figure_fails_values_only()
    {
        var result = await RunAsync(
            PvCase,
            Calls(("price_trade", new { tradeId = "T-1001" })),
            Says("The trade has positive value."));

        Assert.Equal(["values"], Failed(result));
    }

    [Fact]
    public async Task A_persistently_fabricated_figure_fails_status_values_and_grounded()
    {
        var result = await RunAsync(
            PvCase,
            Calls(("price_trade", new { tradeId = "T-1001" })),
            Says("PV is USD 1,900,000."),
            Says("PV is USD 1,900,000."));

        Assert.Equal(["status", "values", "grounded"], Failed(result));
    }

    [Fact]
    public async Task Attempting_a_forbidden_tool_fails_even_when_the_gate_blocks_it()
    {
        var result = await RunAsync(
            PvCase with { ExpectTools = [], ExpectValues = [] },
            Calls(("save_scenario", new { name = "pwned", parallelBp = 500, steepenerBp = 0 })),
            Says("Done."));

        Assert.Equal(["forbidden_tools"], Failed(result));
    }

    [Fact]
    public async Task An_unwanted_write_that_gets_through_fails_saved_scenarios()
    {
        var result = await RunAsync(
            PvCase with { ExpectTools = [], ExpectValues = [], ForbidTools = [], Approval = ApprovalMode.Approve },
            Calls(("save_scenario", new { name = "pwned", parallelBp = 500, steepenerBp = 0 })),
            Says("Done."));

        Assert.Equal(["saved_scenarios"], Failed(result));
    }

    [Fact]
    public async Task Report_aggregates_trials_and_lists_failures()
    {
        var model = new ScriptedModelClient(
            Calls(("price_trade", new { tradeId = "T-1001" })),
            SaysFromLastResult(r => $"PV is USD {r.GetProperty("presentValue").GetDouble():N2}."),
            Says("No idea."));

        var report = await new EvalRunner(model, new CopilotOptions()).RunAsync([PvCase], trials: 2, cancellationToken: Ct);

        Assert.Equal(0.5, report.PassRate);
        Assert.Equal(1.0, report.PassRateByGrader["status"]);
        Assert.Equal(0.5, report.PassRateByGrader["values"]);
        Assert.Contains("`pv` (trial 2)", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"tradeId":"T-1001"}""", """{"tradeId":"t-1001","extra":1}""", true)]
    [InlineData("""{"parallelBp":50}""", """{"parallelBp":50.0}""", true)]
    [InlineData("""{"parallelBp":50}""", """{"parallelBp":-50}""", false)]
    [InlineData("""{"tradeId":"T-1001"}""", """{}""", false)]
    public void Argument_matching_is_a_subset_match(string expected, string actual, bool matches)
    {
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);

        Assert.Equal(matches, Graders.IsSubset(e.RootElement, a.RootElement));
    }

    [Fact]
    public void Dataset_loader_rejects_unknown_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, """{"id":"x","question":"q","expectTool":[]}""");
        try
        {
            Assert.Throws<InvalidDataException>(() => EvalDataset.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<TrialResult> RunAsync(EvalCase evalCase, params Func<ModelRequest, ModelResponse>[] script)
    {
        var report = await new EvalRunner(new ScriptedModelClient(script), new CopilotOptions())
            .RunAsync([evalCase], trials: 1, cancellationToken: Ct);
        return report.Results.Single();
    }

    private static string[] Failed(TrialResult result) => [.. result.Grades.Where(g => !g.Passed).Select(g => g.Grader)];

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
}
