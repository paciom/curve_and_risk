using System.Globalization;
using System.Text.Json;
using CurveRisk.Copilot;

namespace CurveRisk.Evals;

/// <summary>
/// The eval run as a function of its inputs: settings, a model, a clock and two writers. Everything
/// that touches the process (credentials, console, real clock) is supplied by Program.cs, so this can
/// be tested end to end with a scripted model.
/// </summary>
public sealed class EvalCli(IModelClient model, CopilotOptions options, EvalConsole console, TimeProvider clock)
{
    public const int Passed = 0;
    public const int Failed = 1;
    public const int CouldNotRun = 2;

    private static readonly string[] MustPassTags = ["injection", "writes"];

    private readonly TextWriter output = console.Output;
    private readonly TextWriter error = console.Error;

    public async Task<int> RunAsync(EvalSettings settings, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EvalCase> cases;
        try
        {
            cases = LoadCases(settings);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return CouldNotRun;
        }

        await output.WriteLineAsync(
            $"Running {cases.Count} case(s) x {settings.Trials} trial(s) against {options.Model} (effort {options.Effort ?? "default"})...")
            .ConfigureAwait(false);

        var report = await new EvalRunner(model, options)
            .RunAsync(cases, settings.Trials, result => output.WriteLine(Describe(result)), cancellationToken)
            .ConfigureAwait(false);

        await WriteReportAsync(report, settings.OutputDirectory, cancellationToken).ConfigureAwait(false);
        return await VerdictAsync(report, settings.Threshold).ConfigureAwait(false);
    }

    private static IReadOnlyList<EvalCase> LoadCases(EvalSettings settings)
    {
        var cases = EvalDataset.Load(settings.Dataset);
        return settings.Filter is { } tag
            ? [.. cases.Where(c => c.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))]
            : cases;
    }

    private static string Describe(TrialResult result)
    {
        var failed = string.Join(", ", result.Grades.Where(g => !g.Passed).Select(g => g.Grader));
        return result.Passed
            ? $"  PASS  {result.CaseId} #{result.Trial}"
            : $"  FAIL  {result.CaseId} #{result.Trial}  [{failed}]";
    }

    private async Task WriteReportAsync(EvalReport report, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var stamp = clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var markdown = report.ToMarkdown();

        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{stamp}.json"),
            JsonSerializer.Serialize(report, EvalDataset.JsonOptions),
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, "summary.md"), markdown, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(markdown).ConfigureAwait(false);
    }

    /// <summary>Safety cases are checked first and are not averaged; then the overall pass rate.</summary>
    private async Task<int> VerdictAsync(EvalReport report, double threshold)
    {
        var critical = report.FailuresTagged(MustPassTags);
        if (critical.Count > 0)
        {
            var names = string.Join(", ", critical.Select(r => $"{r.CaseId}#{r.Trial}"));
            await error.WriteLineAsync($"Must-pass safety cases failed: {names}.").ConfigureAwait(false);
            return Failed;
        }

        if (report.PassRate < threshold)
        {
            await error.WriteLineAsync($"Pass rate {report.PassRate:P1} is below the threshold {threshold:P1}.").ConfigureAwait(false);
            return Failed;
        }

        return Passed;
    }
}

/// <summary>Where the run writes progress and problems.</summary>
public sealed record EvalConsole(TextWriter Output, TextWriter Error);
