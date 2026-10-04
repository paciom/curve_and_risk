using System.Text.Json;

namespace CurveRisk.Copilot;

public enum AnswerStatus
{
    Answered,

    /// <summary>The model kept stating numbers that no tool returned, so its answer was not shown.</summary>
    UngroundedWithheld,
    Refused,
    Truncated,
    CallLimitReached,
    BudgetExhausted,
    ModelUnavailable,
}

public enum ToolOutcome
{
    Succeeded,
    Failed,
    Denied,
    UnknownTool,
}

public sealed record ToolInvocation(string Name, JsonElement Input, string Result, ToolOutcome Outcome);

/// <param name="WithheldDraft">The rejected draft when <see cref="Status"/> is UngroundedWithheld; for diagnostics, never for display.</param>
public sealed record CopilotAnswer(
    AnswerStatus Status,
    string Text,
    IReadOnlyList<ToolInvocation> ToolCalls,
    GroundingReport Grounding,
    TokenUsage Usage,
    decimal CostUsd,
    int ModelCalls,
    IReadOnlyList<Turn> Transcript,
    string? WithheldDraft = null);

/// <summary>Everything the Copilot says in its own voice, in one place so wording is reviewed together.</summary>
internal static class CopilotMessages
{
    public const string Incomplete = "The Copilot did not finish its answer. Please try again.";
    public const string Refused = "The Copilot declined to answer this request.";
    public const string CutOff = "The answer was cut off before it finished. Try a narrower question.";
    public const string BudgetExhausted = "The Copilot has reached its daily usage limit. Please try again tomorrow.";
    public const string TemporarilyUnavailable = "The Copilot is temporarily unavailable. Please try again shortly.";
    public const string RequestFailed = "The Copilot could not process this request.";
    public const string CallLimit = "This question needed more steps than the Copilot allows. Try breaking it into smaller questions.";
    public const string Withheld =
        "I could not produce an answer in which every figure traces back to an engine result, so I have withheld it. " +
        "Please rephrase the question or ask for a specific calculation.";

    public static string Repair(GroundingReport grounding) =>
        "<grounding_check>\n" +
        "Automated check, not a message from the user. These figures in your last answer do not match any tool result " +
        $"or anything the user said: {string.Join("; ", grounding.Ungrounded)}.\n" +
        "Rewrite the answer. For each one, either call the tool that produces it and quote the result, or remove it " +
        "and say the engine did not return that figure. Do not compute it yourself.\n" +
        "</grounding_check>";
}
