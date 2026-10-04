using System.Diagnostics;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Engine;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests;

/// <summary>Spans and metrics a host sees, named after the OpenTelemetry GenAI semantic conventions.</summary>
public class CopilotTelemetryTests
{
    private const string Model = "telemetry-model";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, object?> ChatTags = new()
    {
        ["gen_ai.operation.name"] = "chat",
        ["gen_ai.provider.name"] = "anthropic",
        ["gen_ai.request.model"] = Model,
        ["gen_ai.response.model"] = "served-model",
    };

    private static CopilotAgent Agent(IModelClient model) => new(
        model,
        new ToolExecutor(ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()), new DenyAllApprovalGate()),
        new CopilotOptions { Model = Model });

    private static Func<ModelRequest, ModelResponse> Responds(StopReason reason) =>
        _ => new ModelResponse([new TextPart("Done.")], reason, new TokenUsage(1200, 80, 900, 30), "served-model");

    [Fact]
    public async Task The_agent_span_names_the_operation_the_agent_the_provider_and_the_outcome()
    {
        using var telemetry = new TelemetryCapture();

        await Agent(new ScriptedModelClient(Says("Done."))).AskAsync("Hi.", cancellationToken: Ct);

        var span = telemetry.Span("invoke_agent risk-copilot");
        Assert.Equal("invoke_agent", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("risk-copilot", span.GetTagItem("gen_ai.agent.name"));
        Assert.Equal("anthropic", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("Answered", span.GetTagItem("curverisk.copilot.status"));
    }

    [Fact]
    public async Task The_chat_span_is_a_client_span_carrying_the_models_and_token_usage()
    {
        using var telemetry = new TelemetryCapture();

        await Agent(new ScriptedModelClient(Responds(StopReason.EndTurn))).AskAsync("Hi.", cancellationToken: Ct);

        var span = telemetry.Span("chat telemetry-model");
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("chat", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("anthropic", span.GetTagItem("gen_ai.provider.name"));
        Assert.Equal(Model, span.GetTagItem("gen_ai.request.model"));
        Assert.Equal("served-model", span.GetTagItem("gen_ai.response.model"));
        Assert.Equal(1200L, span.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(80L, span.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal(900L, span.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
        Assert.Equal(30L, span.GetTagItem("gen_ai.usage.cache_creation.input_tokens"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Theory]
    [InlineData(StopReason.EndTurn, "end_turn")]
    [InlineData(StopReason.ToolUse, "tool_use")]
    [InlineData(StopReason.MaxTokens, "max_tokens")]
    [InlineData(StopReason.Refusal, "refusal")]
    [InlineData(StopReason.Other, "other")]
    public async Task The_chat_span_reports_the_finish_reason_by_its_wire_name(StopReason reason, string expected)
    {
        using var telemetry = new TelemetryCapture();

        await Agent(new ScriptedModelClient(Responds(reason))).AskAsync("Hi.", cancellationToken: Ct);

        var reasons = telemetry.Span("chat telemetry-model").GetTagItem("gen_ai.response.finish_reasons");
        Assert.Equal([expected], Assert.IsType<string[]>(reasons));
    }

    [Fact]
    public async Task Token_usage_is_recorded_once_per_token_type()
    {
        using var telemetry = new TelemetryCapture();

        await Agent(new ScriptedModelClient(Responds(StopReason.EndTurn))).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Collection(
            telemetry.Measurements("gen_ai.client.token.usage"),
            input =>
            {
                Assert.Equal(1200, input.Value);
                Assert.Equal(new Dictionary<string, object?>(ChatTags) { ["gen_ai.token.type"] = "input" }, input.Tags);
            },
            output =>
            {
                Assert.Equal(80, output.Value);
                Assert.Equal(new Dictionary<string, object?>(ChatTags) { ["gen_ai.token.type"] = "output" }, output.Tags);
            });
    }

    [Fact]
    public async Task Estimated_spend_is_counted_in_usd_without_a_token_type()
    {
        using var telemetry = new TelemetryCapture();

        await Agent(new ScriptedModelClient(Responds(StopReason.EndTurn))).AskAsync("Hi.", cancellationToken: Ct);

        // 1200 input at $4, 80 output at $20, 900 cache reads at $0.20 and 30 cache writes at $5, per million.
        var cost = Assert.Single(telemetry.Measurements("curverisk.copilot.cost"));
        Assert.Equal(0.00673, cost.Value, precision: 12);
        Assert.Equal(ChatTags, cost.Tags);
    }

    [Fact]
    public async Task A_provider_failure_marks_the_chat_span_as_an_error_with_the_reason()
    {
        using var telemetry = new TelemetryCapture();
        var model = new ScriptedModelClient(_ => throw new ModelClientException("Rate limited.", isTransient: true, new IOException()));

        await Agent(model).AskAsync("Hi.", cancellationToken: Ct);

        var span = telemetry.Span("chat telemetry-model");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("Rate limited.", span.StatusDescription);
        Assert.Equal("ModelUnavailable", telemetry.Span("invoke_agent risk-copilot").GetTagItem("curverisk.copilot.status"));
        Assert.Empty(telemetry.Measurements("gen_ai.client.token.usage"));
    }

    [Fact]
    public async Task An_ungrounded_answer_adds_an_event_with_the_number_of_figures_and_counts_one_failure()
    {
        using var telemetry = new TelemetryCapture();
        var model = new ScriptedModelClient(Says("DV01 is 123,456 and PV is 987,654."), Says("The engine did not return those."));

        await Agent(model).AskAsync("DV01?", cancellationToken: Ct);

        var failure = Assert.Single(telemetry.Span("invoke_agent risk-copilot").Events);
        Assert.Equal("grounding_failure", failure.Name);
        Assert.Equal([new KeyValuePair<string, object?>("curverisk.ungrounded_numbers", 2)], failure.Tags);
        Assert.Equal(1, Assert.Single(telemetry.Measurements("curverisk.copilot.grounding.failures")).Value);
    }

    [Fact]
    public async Task A_grounded_answer_records_no_grounding_failure()
    {
        using var telemetry = new TelemetryCapture();

        await Agent(new ScriptedModelClient(Says("Nothing to report."))).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Empty(telemetry.Span("invoke_agent risk-copilot").Events);
        Assert.Empty(telemetry.Measurements("curverisk.copilot.grounding.failures"));
    }
}
