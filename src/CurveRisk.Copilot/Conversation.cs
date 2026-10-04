using System.Text.Json;

namespace CurveRisk.Copilot;

public enum TurnRole
{
    User,
    Assistant,
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "Base of a closed set of record types matched by pattern; an interface would lose record equality and the shared base.")]
public abstract record ContentPart;

public sealed record TextPart(string Text) : ContentPart;

/// <summary>Opaque reasoning block. Must be replayed unchanged, signature included, or the API rejects the turn.</summary>
public sealed record ThinkingPart(string Thinking, string Signature) : ContentPart;

public sealed record RedactedThinkingPart(string Data) : ContentPart;

public sealed record ToolCallPart(string Id, string Name, JsonElement Input) : ContentPart;

public sealed record ToolResultPart(string ToolCallId, string Content, bool IsError) : ContentPart;

/// <param name="IsSynthetic">
/// True for user-role turns written by the harness rather than a person (the grounding repair notice).
/// Their text must never count as evidence, or a rejected number would support itself next time round.
/// </param>
public sealed record Turn(TurnRole Role, IReadOnlyList<ContentPart> Parts, bool IsSynthetic = false)
{
    public static Turn UserText(string text) => new(TurnRole.User, [new TextPart(text)]);

    public static Turn HarnessNotice(string text) => new(TurnRole.User, [new TextPart(text)], IsSynthetic: true);
}

public enum StopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    Refusal,
    Other,
}

public sealed record TokenUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens)
{
    public static TokenUsage Zero { get; } = new(0, 0, 0, 0);

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new(
        a.InputTokens + b.InputTokens,
        a.OutputTokens + b.OutputTokens,
        a.CacheReadTokens + b.CacheReadTokens,
        a.CacheWriteTokens + b.CacheWriteTokens);
}

public sealed record ToolSpec(string Name, string Description, JsonElement InputSchema);

public sealed record ModelRequest(string SystemPrompt, IReadOnlyList<ToolSpec> Tools, IReadOnlyList<Turn> Turns);

public sealed record ModelResponse(IReadOnlyList<ContentPart> Parts, StopReason StopReason, TokenUsage Usage, string Model);
