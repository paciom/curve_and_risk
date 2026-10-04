namespace CurveRisk.Copilot;

public sealed record CopilotOptions
{
    public string Model { get; init; } = "claude-opus-5-5";

    /// <summary>
    /// low, medium, high, xhigh or max. Set explicitly rather than relying on a per-model default.
    /// Null omits the field, for compatible endpoints that do not accept it.
    /// </summary>
    public string? Effort { get; init; } = "medium";

    /// <summary>Send cache breakpoints. Off for compatible endpoints that do not implement prompt caching.</summary>
    public bool PromptCaching { get; init; } = true;

    public int MaxOutputTokens { get; init; } = 16_000;

    /// <summary>Upper bound on model calls for one question, including grounding repairs.</summary>
    public int MaxModelCalls { get; init; } = 12;

    /// <summary>How many times the agent may be sent back to fix ungrounded numbers before the answer is withheld.</summary>
    public int MaxGroundingRepairs { get; init; } = 1;

    public ModelPricing Pricing { get; init; } = ModelPricing.ClaudeOpus55;
}
