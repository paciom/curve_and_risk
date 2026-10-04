using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Engine;

namespace CurveRisk.Ai.Tests;

/// <summary>What the model is told, word for word, when a tool call does not run or does not succeed.</summary>
public class ToolExecutorTests
{
    private const string Tool = "stub_tool";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static ToolCallPart Call(string name) => new("call_1", name, JsonSerializer.SerializeToElement(new { }));

    private static async Task<ExecutedTool> RunAsync(Func<object?> invoke, bool requiresApproval = false)
    {
        var executor = new ToolExecutor([new CopilotTool(new StubFunction(Tool, invoke), requiresApproval)], new DenyAllApprovalGate());
        return Assert.Single(await executor.ExecuteAsync([Call(Tool)], Ct));
    }

    [Fact]
    public async Task A_successful_call_returns_the_tool_output_as_a_result_that_is_not_an_error()
    {
        var executed = await RunAsync(() => JsonSerializer.SerializeToElement(new { presentValue = 12.5 }));

        Assert.Equal(new ToolResultPart("call_1", """{"presentValue":12.5}""", IsError: false), executed.Result);
        Assert.Equal((Tool, """{"presentValue":12.5}""", ToolOutcome.Succeeded),
            (executed.Invocation.Name, executed.Invocation.Result, executed.Invocation.Outcome));
    }

    [Fact]
    public async Task An_unknown_tool_is_named_in_the_error()
    {
        var executor = new ToolExecutor(ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()), new DenyAllApprovalGate());

        var executed = Assert.Single(await executor.ExecuteAsync([Call("delete_everything")], Ct));

        Assert.Equal(new ToolResultPart("call_1", "Unknown tool 'delete_everything'.", IsError: true), executed.Result);
        Assert.Equal(ToolOutcome.UnknownTool, executed.Invocation.Outcome);
    }

    [Fact]
    public async Task A_write_with_nobody_to_approve_it_does_not_run_and_says_why()
    {
        var function = new StubFunction(Tool, () => "ran");
        var executor = new ToolExecutor([new CopilotTool(function, RequiresApproval: true)], new DenyAllApprovalGate());

        var executed = Assert.Single(await executor.ExecuteAsync([Call(Tool)], Ct));

        Assert.Equal(0, function.Invocations);
        Assert.Equal(ToolOutcome.Denied, executed.Invocation.Outcome);
        Assert.Equal("Not executed. No user is available to approve changes in this session.", executed.Result.Content);
    }

    [Fact]
    public async Task An_engine_rejection_is_passed_to_the_model_as_written()
    {
        var executed = await RunAsync(() => throw new RiskEngineException("Unknown trade 'T-9999'."));

        Assert.Equal(new ToolResultPart("call_1", "Unknown trade 'T-9999'.", IsError: true), executed.Result);
        Assert.Equal(ToolOutcome.Failed, executed.Invocation.Outcome);
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task Malformed_arguments_are_reported_with_the_reason_so_the_model_can_correct_them(Type failure)
    {
        var executed = await RunAsync(() => throw (Exception)Activator.CreateInstance(failure, "tradeId is missing")!);

        Assert.Equal("Invalid arguments for 'stub_tool': tradeId is missing", executed.Result.Content);
        Assert.True(executed.Result.IsError);
        Assert.Equal(ToolOutcome.Failed, executed.Invocation.Outcome);
    }

    [Fact]
    public async Task An_unexpected_failure_is_reported_without_its_internal_detail()
    {
        var executed = await RunAsync(() => throw new TimeoutException("secret connection string"));

        Assert.Equal(new ToolResultPart("call_1", "'stub_tool' failed unexpectedly.", IsError: true), executed.Result);
        Assert.Equal(ToolOutcome.Failed, executed.Invocation.Outcome);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_tool_failure()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync(() => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task A_denial_reason_from_the_gate_is_passed_to_the_model()
    {
        var gate = new RecordingGate(ApprovalDecision.Deny("The user said no."));
        var executor = new ToolExecutor([new CopilotTool(new StubFunction(Tool, () => "ran"), RequiresApproval: true)], gate);

        var executed = Assert.Single(await executor.ExecuteAsync([Call(Tool)], Ct));

        Assert.Equal(new ToolResultPart("call_1", "Not executed. The user said no.", IsError: true), executed.Result);
    }
}
