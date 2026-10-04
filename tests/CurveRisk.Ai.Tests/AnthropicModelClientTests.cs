using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using CurveRisk.Ai.Tools;
using CurveRisk.Ai.Tools.Fixtures;
using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// Drives the real SDK against a stubbed HTTP transport, so the request body the API would receive and
/// the mapping of its response are both checked without network access. This is the closest thing to
/// a live call that can run in CI without credentials; it does not prove the API accepts the request.
/// </summary>
public class AnthropicModelClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string ToolUseResponse = """
        {
          "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "content": [
            {"type": "thinking", "thinking": "", "signature": "sig-1"},
            {"type": "text", "text": "Pricing it."},
            {"type": "tool_use", "id": "toolu_1", "name": "price_trade", "input": {"tradeId": "T-1001"}, "caller": {"type": "direct"}}
          ],
          "stop_reason": "tool_use", "stop_sequence": null,
          "usage": {"input_tokens": 1200, "output_tokens": 80, "cache_read_input_tokens": 900, "cache_creation_input_tokens": 30}
        }
        """;

    [Fact]
    public async Task Maps_a_tool_use_response()
    {
        var (client, _) = Create(HttpStatusCode.OK, ToolUseResponse);

        var response = await client.CompleteAsync(Request(Turn.UserText("PV of T-1001?")), Ct);

        Assert.Equal(StopReason.ToolUse, response.StopReason);
        Assert.Equal("claude-opus-5-5", response.Model);
        Assert.Equal(new TokenUsage(1200, 80, 900, 30), response.Usage);
        Assert.Collection(
            response.Parts,
            p => Assert.Equal("sig-1", Assert.IsType<ThinkingPart>(p).Signature),
            p => Assert.Equal("Pricing it.", Assert.IsType<TextPart>(p).Text),
            p =>
            {
                var call = Assert.IsType<ToolCallPart>(p);
                Assert.Equal(("toolu_1", "price_trade"), (call.Id, call.Name));
                Assert.Equal("T-1001", call.Input.GetProperty("tradeId").GetString());
            });
    }

    [Fact]
    public async Task Sends_model_effort_cached_system_prompt_and_tool_schemas()
    {
        var (client, handler) = Create(HttpStatusCode.OK, ToolUseResponse);

        await client.CompleteAsync(Request(Turn.UserText("PV of T-1001?")), Ct);

        var body = handler.LastBody.RootElement;
        Assert.EndsWith("/v1/messages", handler.LastUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("claude-opus-5-5", body.GetProperty("model").GetString());
        Assert.Equal("medium", body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.False(body.TryGetProperty("thinking", out _), "thinking is left at the model default");
        Assert.False(body.TryGetProperty("temperature", out _), "sampling parameters are rejected by this model");

        var system = body.GetProperty("system")[0];
        Assert.Equal("SYSTEM", system.GetProperty("text").GetString());
        Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());

        var tools = body.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(6, tools.Count);
        var scenario = tools.Single(t => t.GetProperty("name").GetString() == "run_scenario").GetProperty("input_schema");
        Assert.Equal("object", scenario.GetProperty("type").GetString());
        Assert.Equal("number", scenario.GetProperty("properties").GetProperty("parallelBp").GetProperty("type").GetString());
        Assert.Equal(
            ["tradeId", "parallelBp", "steepenerBp"],
            scenario.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Replays_thinking_tool_use_and_tool_results_in_wire_format()
    {
        var (client, handler) = Create(HttpStatusCode.OK, ToolUseResponse);
        var input = JsonSerializer.SerializeToElement(new { tradeId = "T-1001" });

        await client.CompleteAsync(
            Request(
                Turn.UserText("PV of T-1001?"),
                new Turn(TurnRole.Assistant, [new ThinkingPart(string.Empty, "sig-1"), new ToolCallPart("toolu_1", "price_trade", input)]),
                new Turn(TurnRole.User, [new ToolResultPart("toolu_1", "Unknown trade", IsError: true)])),
            Ct);

        var messages = handler.LastBody.RootElement.GetProperty("messages");
        Assert.Equal(["user", "assistant", "user"], messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()));

        var assistant = messages[1].GetProperty("content");
        Assert.Equal("thinking", assistant[0].GetProperty("type").GetString());
        Assert.Equal("sig-1", assistant[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", assistant[1].GetProperty("type").GetString());
        Assert.Equal("T-1001", assistant[1].GetProperty("input").GetProperty("tradeId").GetString());

        var result = messages[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
        Assert.True(result.GetProperty("is_error").GetBoolean());
    }

    [Theory]
    [InlineData("end_turn", StopReason.EndTurn)]
    [InlineData("max_tokens", StopReason.MaxTokens)]
    [InlineData("refusal", StopReason.Refusal)]
    [InlineData("pause_turn", StopReason.Other)]
    public async Task Maps_stop_reasons(string wire, StopReason expected)
    {
        var json = ToolUseResponse.Replace("\"stop_reason\": \"tool_use\"", $"\"stop_reason\": \"{wire}\"", StringComparison.Ordinal);
        var (client, _) = Create(HttpStatusCode.OK, json);

        var response = await client.CompleteAsync(Request(Turn.UserText("Hi")), Ct);

        Assert.Equal(expected, response.StopReason);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", true)]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", true)]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", false)]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error", false)]
    public async Task Classifies_provider_errors_as_transient_or_not(HttpStatusCode status, string type, bool transient)
    {
        var (client, _) = Create(status, $$"""{"type":"error","error":{"type":"{{type}}","message":"nope"} }""");

        var ex = await Assert.ThrowsAsync<ModelClientException>(() => client.CompleteAsync(Request(Turn.UserText("Hi")), Ct));

        Assert.Equal(transient, ex.IsTransient);
    }

    private static ModelRequest Request(params Turn[] turns) => new(
        "SYSTEM",
        [.. ToolCatalog.Create(new FixtureRiskEngine()).Select(t => new ToolSpec(t.Name, t.Function.Description, t.Function.JsonSchema))],
        turns);

    private static (AnthropicModelClient Client, StubHandler Handler) Create(HttpStatusCode status, string json)
    {
        var handler = new StubHandler(status, json);
        var sdk = new AnthropicClient
        {
            ApiKey = "test-key",
            HttpClient = new HttpClient(handler),
            MaxRetries = 0,
        };
        return (new AnthropicModelClient(sdk, new CopilotOptions()), handler);
    }

    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public JsonDocument LastBody { get; private set; } = JsonDocument.Parse("{}");

        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
