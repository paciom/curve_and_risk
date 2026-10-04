using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// The provider client builds its own SDK client, so these tests point it at a stand-in endpoint on the
/// loopback interface and read what arrives there.
/// </summary>
public class AnthropicProviderClientTests
{
    private const string Response = """
        {
          "id": "msg_1", "type": "message", "role": "assistant", "model": "served-model",
          "content": [{"type": "text", "text": "Done."}], "stop_reason": "end_turn", "stop_sequence": null,
          "usage": {"input_tokens": 12, "output_tokens": 3}
        }
        """;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly ModelRequest Hello = new("SYSTEM", [], [Turn.UserText("Hi")]);

    private static readonly CopilotOptions Compatible = new() { Model = "settings-model", Effort = null, PromptCaching = false };

    [Fact]
    public async Task Requests_go_to_the_configured_base_url_with_the_configured_key()
    {
        using var provider = new LoopbackProvider();
        using var client = new AnthropicProviderClient(new ProviderSettings("configured-key", provider.BaseUrl, Compatible));
        var served = provider.ServeOnceAsync(Response, Ct);

        var response = await client.CompleteAsync(Hello, Ct);

        var request = await served;
        Assert.StartsWith("POST /proxy/v1/messages", request, StringComparison.Ordinal);
        Assert.Contains("x-api-key: configured-key\r\n", request, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("served-model", response.Model);
    }

    [Fact]
    public async Task The_options_in_the_settings_are_used_when_none_are_given()
    {
        using var provider = new LoopbackProvider();
        using var client = new AnthropicProviderClient(new ProviderSettings("configured-key", provider.BaseUrl, Compatible));
        var served = provider.ServeOnceAsync(Response, Ct);

        await client.CompleteAsync(Hello, Ct);

        Assert.Contains("\"model\":\"settings-model\"", await served, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Options_given_explicitly_take_precedence_over_those_in_the_settings()
    {
        using var provider = new LoopbackProvider();
        var settings = new ProviderSettings("configured-key", provider.BaseUrl, Compatible);
        using var client = new AnthropicProviderClient(settings, Compatible with { Model = "explicit-model" });
        var served = provider.ServeOnceAsync(Response, Ct);

        await client.CompleteAsync(Hello, Ct);

        Assert.Contains("\"model\":\"explicit-model\"", await served, StringComparison.Ordinal);
    }
}
