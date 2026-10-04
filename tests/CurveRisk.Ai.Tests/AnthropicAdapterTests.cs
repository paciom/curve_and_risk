using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// The edges of the SDK adapter: how provider failures are worded, what is refused before a request is
/// sent, and the content blocks the agent does not use. Stubbed transport, no network.
/// </summary>
public class AnthropicAdapterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly ModelRequest Hello = new("SYSTEM", [], [Turn.UserText("Hi")]);

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Rate limited by the model provider.", true)]
    [InlineData(HttpStatusCode.InternalServerError, "The model provider returned a server error.", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "The model provider returned a server error.", true)]
    [InlineData(HttpStatusCode.BadRequest, "The model provider rejected the request.", false)]
    [InlineData(HttpStatusCode.Unauthorized, "The model provider rejected the request.", false)]
    public async Task A_provider_error_is_reported_in_plain_words_with_whether_a_retry_may_help(
        HttpStatusCode status, string message, bool transient)
    {
        var client = Client(_ => Json(status, """{"type":"error","error":{"type":"some_error","message":"internal detail"}}"""));

        var ex = await Assert.ThrowsAsync<ModelClientException>(() => client.CompleteAsync(Hello, Ct));

        Assert.Equal((message, transient), (ex.Message, ex.IsTransient));
    }

    [Fact]
    public async Task An_unreachable_provider_is_a_transient_failure()
    {
        var client = Client(_ => throw new HttpRequestException("No such host is known."));

        var ex = await Assert.ThrowsAsync<ModelClientException>(() => client.CompleteAsync(Hello, Ct));

        Assert.Equal(("Could not reach the model provider.", true), (ex.Message, ex.IsTransient));
    }

    [Fact]
    public async Task An_unknown_effort_level_is_refused_by_name_before_anything_is_sent()
    {
        var handler = new RespondingHandler(_ => Json(HttpStatusCode.OK, TextResponse("[]")));
        var client = Client(handler, new CopilotOptions { Effort = "extreme" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.CompleteAsync(Hello, Ct));

        Assert.StartsWith("Unknown effort level 'extreme'.", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task A_content_part_the_adapter_does_not_know_is_refused_by_name()
    {
        var client = Client(_ => Json(HttpStatusCode.OK, TextResponse("[]")));
        var request = Hello with { Turns = [new Turn(TurnRole.User, [new AlienPart()])] };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => client.CompleteAsync(request, Ct));

        Assert.Equal("Unsupported content part AlienPart.", ex.Message);
    }

    [Fact]
    public async Task Redacted_thinking_is_read_from_a_response_and_replayed_unchanged()
    {
        var handler = new RespondingHandler(_ => Json(HttpStatusCode.OK, TextResponse("""[{"type":"redacted_thinking","data":"opaque"}]""")));
        var client = Client(handler);

        var response = await client.CompleteAsync(Hello, Ct);
        await client.CompleteAsync(Hello with { Turns = [Turn.UserText("Hi"), new Turn(TurnRole.Assistant, response.Parts)] }, Ct);

        Assert.Equal([new RedactedThinkingPart("opaque")], response.Parts);
        var replayed = handler.LastBody.RootElement.GetProperty("messages")[1].GetProperty("content")[0];
        Assert.Equal("redacted_thinking", replayed.GetProperty("type").GetString());
        Assert.Equal("opaque", replayed.GetProperty("data").GetString());
    }

    [Fact]
    public async Task Server_tool_blocks_are_dropped_from_a_response()
    {
        const string content = """
            [{"type":"server_tool_use","id":"srvtoolu_1","name":"web_search","input":{"query":"rates"},"caller":{"type":"direct"}},
             {"type":"text","text":"Done."}]
            """;
        var client = Client(_ => Json(HttpStatusCode.OK, TextResponse(content)));

        var response = await client.CompleteAsync(Hello, Ct);

        Assert.Equal([new TextPart("Done.")], response.Parts);
    }

    [Fact]
    public async Task Usage_without_cache_counts_is_read_as_zero_cache_tokens()
    {
        var client = Client(_ => Json(HttpStatusCode.OK, TextResponse("[]")));

        var response = await client.CompleteAsync(Hello, Ct);

        Assert.Equal(new TokenUsage(12, 3, 0, 0), response.Usage);
    }

    [Fact]
    public async Task A_tool_without_parameters_is_sent_with_an_empty_object_schema()
    {
        var handler = new RespondingHandler(_ => Json(HttpStatusCode.OK, TextResponse("[]")));
        var ping = new ToolSpec("ping", "Checks the engine is up.", JsonSerializer.Deserialize<JsonElement>("""{"type":"object"}"""));

        await Client(handler).CompleteAsync(Hello with { Tools = [ping] }, Ct);

        var tool = handler.LastBody.RootElement.GetProperty("tools")[0];
        Assert.Equal(("ping", "Checks the engine is up."), (tool.GetProperty("name").GetString(), tool.GetProperty("description").GetString()));
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
        Assert.Empty(tool.GetProperty("input_schema").GetProperty("properties").EnumerateObject());
    }

    private static string TextResponse(string contentJson) => $$"""
        {
          "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "content": {{contentJson}}, "stop_reason": "end_turn", "stop_sequence": null,
          "usage": {"input_tokens": 12, "output_tokens": 3}
        }
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AnthropicModelClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        Client(new RespondingHandler(respond));

    private static AnthropicModelClient Client(RespondingHandler handler, CopilotOptions? options = null)
    {
        var sdk = new AnthropicClient { ApiKey = "test-key", HttpClient = new HttpClient(handler), MaxRetries = 0 };
        return new AnthropicModelClient(sdk, options ?? new CopilotOptions());
    }

    private sealed record AlienPart : ContentPart;

    private sealed class RespondingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public JsonDocument LastBody { get; private set; } = JsonDocument.Parse("{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
