using System.Globalization;
using System.Text;
using CurveRisk.Ai.Tools;
using CurveRisk.Ai.Tools.Fixtures;
using CurveRisk.Copilot;

namespace CurveRisk.Evals;

public sealed record TrialResult(
    string CaseId,
    IReadOnlyList<string> Tags,
    int Trial,
    bool Passed,
    IReadOnlyList<Grade> Grades,
    AnswerStatus Status,
    string Answer,
    IReadOnlyList<string> ToolCalls,
    int ModelCalls,
    TokenUsage Usage,
    decimal CostUsd)
{
    public static TrialResult From(EvalCase evalCase, int trial, CopilotAnswer answer, IReadOnlyList<Grade> grades) => new(
        evalCase.Id,
        evalCase.Tags,
        trial,
        grades.All(g => g.Passed),
        grades,
        answer.Status,
        answer.Text,
        [.. answer.ToolCalls.Select(c => $"{c.Name}{c.Input.GetRawText()} -> {c.Outcome}")],
        answer.ModelCalls,
        answer.Usage,
        answer.CostUsd);
}

public sealed record EvalReport(string Model, string Effort, int Trials, IReadOnlyList<TrialResult> Results)
{
    public double PassRate => Results.Count == 0 ? 0 : (double)Results.Count(r => r.Passed) / Results.Count;

    public decimal TotalCostUsd => Results.Sum(r => r.CostUsd);

    /// <summary>
    /// Safety cases are not averaged. One failed run of a case tagged with any of <paramref name="tags"/>
    /// fails the suite whatever the overall pass rate, because "resists injection 90% of the time" is not a pass.
    /// </summary>
    public IReadOnlyList<TrialResult> FailuresTagged(IReadOnlyCollection<string> tags) =>
        [.. Results.Where(r => !r.Passed && r.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)))];

    public IReadOnlyDictionary<string, double> PassRateByGrader => Results
        .SelectMany(r => r.Grades)
        .GroupBy(g => g.Grader, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => (double)g.Count(x => x.Passed) / g.Count(), StringComparer.Ordinal);

    public string ToMarkdown()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Copilot eval report").AppendLine();
        sb.AppendLine(inv, $"Model `{Model}`, effort `{Effort}`, {Trials} trial(s) per case, {Results.Count} runs.").AppendLine();
        sb.AppendLine(inv, $"**Pass rate: {PassRate:P1}** ({Results.Count(r => r.Passed)}/{Results.Count}). Estimated cost: ${TotalCostUsd:F4}.").AppendLine();

        sb.AppendLine("| Grader | Pass rate |").AppendLine("|---|---|");
        foreach (var (grader, rate) in PassRateByGrader.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(inv, $"| {grader} | {rate:P1} |");
        }

        var failures = Results.Where(r => !r.Passed).ToList();
        if (failures.Count > 0)
        {
            sb.AppendLine().AppendLine("## Failures").AppendLine();
            foreach (var failure in failures)
            {
                var reasons = failure.Grades.Where(g => !g.Passed).Select(g => $"{g.Grader}: {g.Detail}");
                sb.AppendLine(inv, $"- `{failure.CaseId}` (trial {failure.Trial}): {string.Join(" | ", reasons)}");
            }
        }

        return sb.ToString();
    }
}

public sealed class EvalRunner(IModelClient model, CopilotOptions options)
{
    public async Task<EvalReport> RunAsync(
        IReadOnlyList<EvalCase> cases,
        int trials,
        Action<TrialResult>? onResult = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TrialResult>();
        foreach (var evalCase in cases)
        {
            // Model output varies run to run; a case that passes once has not been shown to pass.
            for (var trial = 1; trial <= trials; trial++)
            {
                var result = await RunOneAsync(evalCase, trial, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                onResult?.Invoke(result);
            }
        }

        return new EvalReport(options.Model, options.Effort, trials, results);
    }

    private async Task<TrialResult> RunOneAsync(EvalCase evalCase, int trial, CancellationToken cancellationToken)
    {
        // Fresh engine per run so one case's writes cannot leak into the next.
        var engine = new FixtureRiskEngine();
        var tools = new ToolExecutor(ToolCatalog.Create(engine), new FixedApprovalGate(evalCase.Approval));
        var agent = new CopilotAgent(model, tools, options);

        var answer = await agent.AskAsync(evalCase.Question, cancellationToken: cancellationToken).ConfigureAwait(false);
        var grades = await Graders.GradeAsync(evalCase, answer, engine, cancellationToken).ConfigureAwait(false);

        return TrialResult.From(evalCase, trial, answer, grades);
    }

    private sealed class FixedApprovalGate(ApprovalMode mode) : IApprovalGate
    {
        public Task<ApprovalDecision> RequestAsync(ToolCallPart toolCall, CancellationToken cancellationToken) =>
            Task.FromResult(mode == ApprovalMode.Approve
                ? ApprovalDecision.Approve
                : ApprovalDecision.Deny("The user declined this change."));
    }
}
