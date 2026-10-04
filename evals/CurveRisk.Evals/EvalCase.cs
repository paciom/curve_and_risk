using System.Text.Json;
using System.Text.Json.Serialization;
using CurveRisk.Copilot;

namespace CurveRisk.Evals;

/// <summary>One tool call the agent is expected to make. <see cref="Args"/> is matched as a subset.</summary>
public sealed record ExpectedTool(string Name, JsonElement? Args = null);

/// <summary>
/// A figure the answer must state. The expected value is not stored in the dataset: the grader calls
/// the engine itself at grading time, so the dataset cannot drift out of date when the engine changes.
/// </summary>
public sealed record ExpectedValue(string Tool, JsonElement Args, string Path);

public enum ApprovalMode
{
    Deny,
    Approve,
}

public sealed record EvalCase
{
    public required string Id { get; init; }

    public required string Question { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public AnswerStatus ExpectStatus { get; init; } = AnswerStatus.Answered;

    /// <summary>Tools that must have been called successfully.</summary>
    public IReadOnlyList<ExpectedTool> ExpectTools { get; init; } = [];

    /// <summary>Tools the agent must not even attempt, whether or not the approval gate would have stopped them.</summary>
    public IReadOnlyList<string> ForbidTools { get; init; } = [];

    public IReadOnlyList<ExpectedValue> ExpectValues { get; init; } = [];

    /// <summary>How the simulated user responds to approval prompts.</summary>
    public ApprovalMode Approval { get; init; } = ApprovalMode.Deny;

    /// <summary>Number of scenarios that should exist in the engine afterwards. Catches unwanted writes.</summary>
    public int ExpectSavedScenarios { get; init; }
}

public static class EvalDataset
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static IReadOnlyList<EvalCase> Load(string path)
    {
        var cases = new List<EvalCase>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                cases.Add(JsonSerializer.Deserialize<EvalCase>(line, JsonOptions)
                    ?? throw new JsonException("Line deserialised to null."));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{path}:{lineNumber}: {ex.Message}", ex);
            }
        }

        var duplicate = cases.GroupBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null
            ? cases
            : throw new InvalidDataException($"{path}: duplicate case id '{duplicate.Key}'.");
    }
}
