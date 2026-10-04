namespace CurveRisk.Copilot;

/// <summary>
/// Which model endpoint the Copilot talks to. Anthropic is the default; any service that implements the
/// Anthropic Messages API (z.ai is one) works by changing the base URL, key and model name.
///
/// Read from named variables through a lookup function, never from the process environment directly,
/// so the choice is testable and the library stays free of ambient state.
/// </summary>
public sealed record ProviderSettings(string ApiKey, Uri? BaseUrl, CopilotOptions Options)
{
    public const string ProviderVariable = "COPILOT_PROVIDER";
    public const string ModelVariable = "COPILOT_MODEL";
    public const string BaseUrlVariable = "COPILOT_BASE_URL";
    public const string AnthropicKeyVariable = "ANTHROPIC_API_KEY";
    public const string ZaiKeyVariable = "ZAI_API_KEY";

    /// <summary>z.ai's Anthropic-compatible endpoint. Override with COPILOT_BASE_URL if it moves.</summary>
    public static readonly Uri ZaiBaseUrl = new("https://api.z.ai/api/anthropic");

    /// <summary>Returns null, with a reason, when no usable credentials are configured.</summary>
    public static ProviderSettings? FromVariables(Func<string, string?> variable, out string problem)
    {
        var provider = (variable(ProviderVariable) ?? InferProvider(variable)).Trim().ToLowerInvariant();
        return provider switch
        {
            "anthropic" => Anthropic(variable, out problem),
            "zai" => Zai(variable, out problem),
            _ => Fail($"Unknown {ProviderVariable} '{provider}'. Use 'anthropic' or 'zai'.", out problem),
        };
    }

    private static string InferProvider(Func<string, string?> variable) =>
        string.IsNullOrEmpty(variable(AnthropicKeyVariable)) && !string.IsNullOrEmpty(variable(ZaiKeyVariable))
            ? "zai"
            : "anthropic";

    private static ProviderSettings? Anthropic(Func<string, string?> variable, out string problem)
    {
        var key = variable(AnthropicKeyVariable);
        if (string.IsNullOrEmpty(key))
        {
            return Fail($"{AnthropicKeyVariable} is not set.", out problem);
        }

        var defaults = new CopilotOptions();
        problem = string.Empty;
        return new ProviderSettings(key, BaseUrlOverride(variable), defaults with { Model = variable(ModelVariable) ?? defaults.Model });
    }

    private static ProviderSettings? Zai(Func<string, string?> variable, out string problem)
    {
        var key = variable(ZaiKeyVariable);
        var model = variable(ModelVariable);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
        {
            return Fail($"The zai provider needs both {ZaiKeyVariable} and {ModelVariable} (the model name is not guessed).", out problem);
        }

        // A compatible endpoint is not assumed to accept Anthropic-only request fields, and its prices
        // are not known here, so cost is reported as zero rather than as a wrong number.
        var options = new CopilotOptions
        {
            Model = model,
            Effort = null,
            PromptCaching = false,
            Pricing = ModelPricing.Unknown,
        };
        problem = string.Empty;
        return new ProviderSettings(key, BaseUrlOverride(variable) ?? ZaiBaseUrl, options);
    }

    private static Uri? BaseUrlOverride(Func<string, string?> variable) =>
        variable(BaseUrlVariable) is { Length: > 0 } url ? new Uri(url) : null;

    private static ProviderSettings? Fail(string reason, out string problem)
    {
        problem = reason;
        return null;
    }
}
