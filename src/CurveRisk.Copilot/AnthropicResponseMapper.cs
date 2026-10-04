using System.Text.Json;
using Sdk = Anthropic.Models.Messages;

namespace CurveRisk.Copilot;

/// <summary>Reads a Messages API response into the agent's own conversation model.</summary>
internal static class AnthropicResponseMapper
{
    public static ModelResponse ToResponse(Sdk.Message message) => new(
        [.. message.Content.Select(FromBlock).OfType<ContentPart>()],
        MapStopReason(message),
        new TokenUsage(
            message.Usage.InputTokens,
            message.Usage.OutputTokens,
            message.Usage.CacheReadInputTokens ?? 0,
            message.Usage.CacheCreationInputTokens ?? 0),
        message.Model);

    private static StopReason MapStopReason(Sdk.Message message)
    {
        if (message.StopReason == "end_turn")
        {
            return StopReason.EndTurn;
        }

        if (message.StopReason == "tool_use")
        {
            return StopReason.ToolUse;
        }

        if (message.StopReason == "max_tokens")
        {
            return StopReason.MaxTokens;
        }

        return message.StopReason == "refusal" ? StopReason.Refusal : StopReason.Other;
    }

    private static ContentPart? FromBlock(Sdk.ContentBlock block)
    {
        if (block.TryPickText(out var text))
        {
            return new TextPart(text.Text);
        }

        if (block.TryPickThinking(out var thinking))
        {
            return new ThinkingPart(thinking.Thinking, thinking.Signature);
        }

        if (block.TryPickRedactedThinking(out var redacted))
        {
            return new RedactedThinkingPart(redacted.Data);
        }

        if (block.TryPickToolUse(out var toolUse))
        {
            return new ToolCallPart(toolUse.ID, toolUse.Name, JsonSerializer.SerializeToElement(toolUse.Input));
        }

        // Server-tool blocks are not used by this agent.
        return null;
    }
}
