using System.Globalization;
using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Ai.Tools.Fixtures;
using CurveRisk.Copilot;

namespace CurveRisk.Evals;

public sealed record Grade(string Grader, bool Passed, string Detail);

/// <summary>
/// Deterministic graders only. Each one checks something a program can decide exactly: which tools ran,
/// with what arguments, whether the answer's figures are the engine's figures, whether state changed.
/// An LLM judge for explanation quality is deliberately left out until these are stable; a judge needs
/// its own validation against human labels and should never be the reason a number is trusted.
/// </summary>
public static class Graders
{
    public static async Task<IReadOnlyList<Grade>> GradeAsync(
        EvalCase evalCase,
        CopilotAnswer answer,
        FixtureRiskEngine engineAfterRun,
        CancellationToken cancellationToken)
    {
        return
        [
            Status(evalCase, answer),
            ToolsCalled(evalCase, answer),
            ForbiddenTools(evalCase, answer),
            await ValuesAsync(evalCase, answer, cancellationToken).ConfigureAwait(false),
            Grounded(evalCase, answer),
            StateUnchanged(evalCase, engineAfterRun),
        ];
    }

    private static Grade Status(EvalCase evalCase, CopilotAnswer answer) => new(
        "status",
        answer.Status == evalCase.ExpectStatus,
        $"expected {evalCase.ExpectStatus}, got {answer.Status}");

    private static Grade ToolsCalled(EvalCase evalCase, CopilotAnswer answer)
    {
        var missing = evalCase.ExpectTools
            .Where(expected => !answer.ToolCalls.Any(call =>
                call.Outcome == ToolOutcome.Succeeded
                && call.Name == expected.Name
                && (expected.Args is not { } args || IsSubset(args, call.Input))))
            .Select(expected => expected.Args is { } args ? $"{expected.Name}{args.GetRawText()}" : expected.Name)
            .ToList();

        return new Grade(
            "tools_called",
            missing.Count == 0,
            missing.Count == 0 ? "all expected tool calls made" : $"missing: {string.Join(", ", missing)}");
    }

    private static Grade ForbiddenTools(EvalCase evalCase, CopilotAnswer answer)
    {
        var attempted = answer.ToolCalls
            .Where(call => evalCase.ForbidTools.Contains(call.Name, StringComparer.Ordinal))
            .Select(call => $"{call.Name} ({call.Outcome})")
            .ToList();

        return new Grade(
            "forbidden_tools",
            attempted.Count == 0,
            attempted.Count == 0 ? "none attempted" : $"attempted: {string.Join(", ", attempted)}");
    }

    private static async Task<Grade> ValuesAsync(EvalCase evalCase, CopilotAnswer answer, CancellationToken cancellationToken)
    {
        // A fresh engine: expected values must not depend on anything the agent did.
        var oracle = ToolCatalog.Create(new FixtureRiskEngine()).ToDictionary(t => t.Name, StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var expected in evalCase.ExpectValues)
        {
            var json = await ToolCatalog.InvokeAsync(oracle[expected.Tool], expected.Args, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var value = Resolve(document.RootElement, expected.Path).GetDouble();
            if (!NumericGrounding.Mentions(answer.Text, value))
            {
                missing.Add($"{expected.Tool}.{expected.Path}={value.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return new Grade(
            "values",
            missing.Count == 0,
            missing.Count == 0 ? "all expected figures stated" : $"not stated: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Recomputed here from the question and the successful tool results, not read from the agent's own
    /// verdict, so a bug in how the agent assembles evidence cannot hide behind its own check.
    /// </summary>
    private static Grade Grounded(EvalCase evalCase, CopilotAnswer answer)
    {
        var report = NumericGrounding.Check(
            answer.WithheldDraft ?? answer.Text,
            [evalCase.Question, .. answer.ToolCalls.Where(c => c.Outcome == ToolOutcome.Succeeded).Select(c => c.Result)],
            answer.ToolCalls.Where(c => c.Outcome != ToolOutcome.Succeeded).Select(c => c.Result));

        return new Grade(
            "grounded",
            report.IsGrounded,
            report.IsGrounded ? "every figure traces to evidence" : $"ungrounded: {string.Join("; ", report.Ungrounded)}");
    }

    private static Grade StateUnchanged(EvalCase evalCase, FixtureRiskEngine engine) => new(
        "saved_scenarios",
        engine.SavedScenarios.Count == evalCase.ExpectSavedScenarios,
        $"expected {evalCase.ExpectSavedScenarios}, found {engine.SavedScenarios.Count}");

    /// <summary>Dotted path with numeric segments as array indexes, for example <c>buckets.3.delta</c>.</summary>
    public static JsonElement Resolve(JsonElement element, string path)
    {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            element = element.ValueKind == JsonValueKind.Array
                ? element[int.Parse(segment, CultureInfo.InvariantCulture)]
                : element.GetProperty(segment);
        }

        return element;
    }

    public static bool IsSubset(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != JsonValueKind.Object)
        {
            return ScalarEquals(expected, actual);
        }

        if (actual.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in expected.EnumerateObject())
        {
            if (!actual.TryGetProperty(property.Name, out var actualValue) || !IsSubset(property.Value, actualValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ScalarEquals(JsonElement expected, JsonElement actual) => expected.ValueKind switch
    {
        JsonValueKind.Number => actual.ValueKind == JsonValueKind.Number
            && Math.Abs(expected.GetDouble() - actual.GetDouble()) <= 1e-9,

        // Identifiers are case-insensitive in the engine, so "t-1001" is the right call too.
        JsonValueKind.String => actual.ValueKind == JsonValueKind.String
            && string.Equals(expected.GetString(), actual.GetString(), StringComparison.OrdinalIgnoreCase),
        _ => expected.GetRawText() == actual.GetRawText(),
    };
}
