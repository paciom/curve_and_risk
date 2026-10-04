using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CurveRisk.Copilot;

/// <summary>
/// Traces and metrics for the agent, named after the OpenTelemetry GenAI semantic conventions so that
/// any OTel backend (the Aspire dashboard included) renders model calls and tool calls natively.
/// A host opts in with <c>AddSource(CopilotTelemetry.Name)</c> and <c>AddMeter(CopilotTelemetry.Name)</c>.
///
/// Prompts, answers and tool payloads are deliberately not recorded on spans: they can carry client data.
/// </summary>
public static class CopilotTelemetry
{
    public const string Name = "CurveRisk.Copilot";
    private const string Provider = "anthropic";
    private const string OperationNameTag = "gen_ai.operation.name";

    private static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);

    private static readonly Histogram<long> TokenUsageHistogram = Meter.CreateHistogram<long>(
        "gen_ai.client.token.usage", unit: "{token}", description: "Tokens used per model call.");

    private static readonly Counter<double> CostCounter = Meter.CreateCounter<double>(
        "curverisk.copilot.cost", unit: "USD", description: "Estimated model spend.");

    private static readonly Counter<long> GroundingFailureCounter = Meter.CreateCounter<long>(
        "curverisk.copilot.grounding.failures", unit: "{answer}", description: "Answers containing numbers not traceable to a tool result.");

    private static readonly Counter<long> ToolCallCounter = Meter.CreateCounter<long>(
        "curverisk.copilot.tool.calls", unit: "{call}", description: "Tool calls by tool and outcome.");

    public static Activity? StartAgent() =>
        Source.StartActivity("invoke_agent risk-copilot")
            ?.SetTag(OperationNameTag, "invoke_agent")
            .SetTag("gen_ai.agent.name", "risk-copilot")
            .SetTag("gen_ai.provider.name", Provider);

    public static Activity? StartChat(string model) =>
        Source.StartActivity($"chat {model}", ActivityKind.Client)
            ?.SetTag(OperationNameTag, "chat")
            .SetTag("gen_ai.provider.name", Provider)
            .SetTag("gen_ai.request.model", model);

    public static Activity? StartTool(ToolCallPart call) =>
        Source.StartActivity($"execute_tool {call.Name}")
            ?.SetTag(OperationNameTag, "execute_tool")
            .SetTag("gen_ai.tool.name", call.Name)
            .SetTag("gen_ai.tool.call.id", call.Id);

    public static void RecordChat(Activity? activity, string requestModel, ModelResponse response, decimal costUsd)
    {
        activity?.SetTag("gen_ai.response.model", response.Model)
            .SetTag("gen_ai.response.finish_reasons", new[] { WireName(response.StopReason) })
            .SetTag("gen_ai.usage.input_tokens", response.Usage.InputTokens)
            .SetTag("gen_ai.usage.output_tokens", response.Usage.OutputTokens)
            .SetTag("gen_ai.usage.cache_read.input_tokens", response.Usage.CacheReadTokens)
            .SetTag("gen_ai.usage.cache_creation.input_tokens", response.Usage.CacheWriteTokens);

        var tags = new TagList
        {
            { OperationNameTag, "chat" },
            { "gen_ai.provider.name", Provider },
            { "gen_ai.request.model", requestModel },
            { "gen_ai.response.model", response.Model },
        };
        var input = tags;
        input.Add("gen_ai.token.type", "input");
        var output = tags;
        output.Add("gen_ai.token.type", "output");
        TokenUsageHistogram.Record(response.Usage.InputTokens, input);
        TokenUsageHistogram.Record(response.Usage.OutputTokens, output);
        CostCounter.Add((double)costUsd, tags);
    }

    private static string WireName(StopReason reason) => reason switch
    {
        StopReason.EndTurn => "end_turn",
        StopReason.ToolUse => "tool_use",
        StopReason.MaxTokens => "max_tokens",
        StopReason.Refusal => "refusal",
        _ => "other",
    };

    public static void RecordTool(Activity? activity, string toolName, ToolOutcome outcome)
    {
        if (outcome != ToolOutcome.Succeeded)
        {
            activity?.SetStatus(ActivityStatusCode.Error, outcome.ToString());
        }

        ToolCallCounter.Add(1, new("gen_ai.tool.name", toolName), new("outcome", outcome.ToString()));
    }

    public static void RecordGroundingFailure(Activity? activity, int ungroundedCount)
    {
        activity?.AddEvent(new ActivityEvent(
            "grounding_failure",
            tags: new ActivityTagsCollection { ["curverisk.ungrounded_numbers"] = ungroundedCount }));
        GroundingFailureCounter.Add(1);
    }
}
