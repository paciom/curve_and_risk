using Sdk = Anthropic.Models.Messages;

namespace CurveRisk.Copilot;

/// <summary>Builds a Messages API request from the agent's own conversation model.</summary>
internal static class AnthropicRequestMapper
{
    public static Sdk.MessageCreateParams ToParameters(ModelRequest request, CopilotOptions options) => new()
    {
        Model = options.Model,
        MaxTokens = options.MaxOutputTokens,

        // Thinking is left at the model default (adaptive); effort is the depth control.
        OutputConfig = new Sdk.OutputConfig { Effort = ParseEffort(options.Effort) },

        // Render order is tools -> system -> messages. The breakpoint on the system block caches the
        // stable prefix shared by every conversation; the top-level one caches the growing history
        // between iterations of the same loop. Hits only start once the prefix passes the model's
        // minimum cacheable length, so verify with Usage.CacheReadInputTokens rather than assume.
        System = new List<Sdk.TextBlockParam>
        {
            new() { Text = request.SystemPrompt, CacheControl = new Sdk.CacheControlEphemeral() },
        },
        CacheControl = new Sdk.CacheControlEphemeral(),
        Tools = [.. request.Tools.Select(ToTool)],
        Messages = [.. request.Turns.Select(ToMessage)],
    };

    private static Sdk.Effort ParseEffort(string effort) =>
        Enum.TryParse<Sdk.Effort>(effort, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown effort level '{effort}'.", nameof(effort));

    private static Sdk.ToolUnion ToTool(ToolSpec spec)
    {
        var properties = spec.InputSchema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : [];
        var required = spec.InputSchema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(r => r.GetString()!).ToList()
            : [];

        return new Sdk.Tool
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    private static Sdk.MessageParam ToMessage(Turn turn) => new()
    {
        Role = turn.Role == TurnRole.User ? Sdk.Role.User : Sdk.Role.Assistant,
        Content = turn.Parts.Select(ToBlock).ToList(),
    };

    private static Sdk.ContentBlockParam ToBlock(ContentPart part) => part switch
    {
        TextPart text => new Sdk.TextBlockParam { Text = text.Text },
        ThinkingPart thinking => new Sdk.ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature },
        RedactedThinkingPart redacted => new Sdk.RedactedThinkingBlockParam { Data = redacted.Data },
        ToolCallPart toolCall => new Sdk.ToolUseBlockParam
        {
            ID = toolCall.Id,
            Name = toolCall.Name,
            Input = toolCall.Input.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
        },
        ToolResultPart result => new Sdk.ToolResultBlockParam
        {
            ToolUseID = result.ToolCallId,
            Content = result.Content,
            IsError = result.IsError,
        },
        _ => throw new NotSupportedException($"Unsupported content part {part.GetType().Name}."),
    };
}
