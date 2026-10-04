using System.Diagnostics;
using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Engine;

namespace CurveRisk.Ai.Tests;

/// <summary>The span and the counter recorded for each tool call.</summary>
public class ToolTelemetryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static ToolExecutor Executor() =>
        new(ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()), new DenyAllApprovalGate());

    private static ToolCallPart Call(string name, string argumentsJson) =>
        new("call_7", name, JsonSerializer.Deserialize<JsonElement>(argumentsJson));

    [Fact]
    public async Task A_tool_span_names_the_tool_and_the_call_and_is_not_an_error_when_the_tool_succeeds()
    {
        using var telemetry = new TelemetryCapture();

        await Executor().ExecuteAsync([Call("price_trade", """{"tradeId":"T-1001"}""")], Ct);

        var span = telemetry.Span("execute_tool price_trade");
        Assert.Equal("execute_tool", span.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("price_trade", span.GetTagItem("gen_ai.tool.name"));
        Assert.Equal("call_7", span.GetTagItem("gen_ai.tool.call.id"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Null(span.StatusDescription);
    }

    [Theory]
    [InlineData("price_trade", """{"tradeId":"T-9999"}""", "Failed")]
    [InlineData("save_scenario", """{"name":"Bear","parallelBp":50,"steepenerBp":0}""", "Denied")]
    [InlineData("delete_everything", "{}", "UnknownTool")]
    public async Task A_tool_that_does_not_succeed_marks_its_span_as_an_error_named_after_the_outcome(
        string tool, string argumentsJson, string outcome)
    {
        using var telemetry = new TelemetryCapture();

        await Executor().ExecuteAsync([Call(tool, argumentsJson)], Ct);

        var span = telemetry.Span($"execute_tool {tool}");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(outcome, span.StatusDescription);
    }

    [Theory]
    [InlineData("price_trade", """{"tradeId":"T-1001"}""", "Succeeded")]
    [InlineData("price_trade", """{"tradeId":"T-9999"}""", "Failed")]
    [InlineData("save_scenario", """{"name":"Bear","parallelBp":50,"steepenerBp":0}""", "Denied")]
    [InlineData("delete_everything", "{}", "UnknownTool")]
    public async Task Every_tool_call_is_counted_once_by_tool_and_outcome(string tool, string argumentsJson, string outcome)
    {
        using var telemetry = new TelemetryCapture();

        await Executor().ExecuteAsync([Call(tool, argumentsJson)], Ct);

        var counted = Assert.Single(telemetry.Measurements("curverisk.copilot.tool.calls"));
        Assert.Equal(1, counted.Value);
        Assert.Equal(new Dictionary<string, object?> { ["gen_ai.tool.name"] = tool, ["outcome"] = outcome }, counted.Tags);
    }
}
