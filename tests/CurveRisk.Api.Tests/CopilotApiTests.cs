using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CurveRisk.Contracts.V1;
using CurveRisk.Copilot;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CurveRisk.Api.Tests;

/// <summary>
/// The Copilot over HTTP, with the model replaced by a script. Everything else is real: the tools
/// price stored trades on a stored snapshot, and the guard rails decide what is returned.
/// </summary>
public sealed class CopilotApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly TokenUsage Usage = new(1000, 100, 0, 0);

    [Fact]
    public async Task An_answer_quoting_an_engine_figure_is_returned_with_the_tools_it_used_and_what_it_cost()
    {
        var client = ClientWith(
            Calls("price_trade", new { tradeId = "C-1" }),
            Says("The PV of C-1 is USD 1,270,062.29."));
        var snapshotId = await SnapshotWithTradeAsync(client, "C-1");

        var response = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(snapshotId, "What is the PV of C-1?"), Ct);
        var answer = await response.Content.ReadFromJsonAsync<CopilotAnswerResponse>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Answered", "The PV of C-1 is USD 1,270,062.29.", 2), (answer!.Status, answer.Answer, answer.ModelCalls));
        Assert.Equal(new CopilotToolCallDto("price_trade", "Succeeded"), Assert.Single(answer.ToolCalls));
        Assert.Empty(answer.UngroundedFigures);
        Assert.Equal((2000, 200, 0.012m), (answer.InputTokens, answer.OutputTokens, answer.CostUsd));
    }

    [Fact]
    public async Task A_figure_the_engine_never_produced_is_withheld_and_named()
    {
        var client = ClientWith(Says("The PV is USD 9,876,543."), Says("The PV is USD 9,876,543."));
        var snapshotId = await SnapshotWithTradeAsync(client, "C-2");

        var answer = await AskAsync(client, snapshotId, "What is the PV of C-2?");

        Assert.Equal("UngroundedWithheld", answer.Status);
        Assert.DoesNotContain("9,876,543", answer.Answer, StringComparison.Ordinal);
        Assert.Equal(["USD 9,876,543"], answer.UngroundedFigures);
    }

    [Fact]
    public async Task A_write_requested_over_http_is_declined_because_nobody_can_approve_it()
    {
        var client = ClientWith(
            Calls("save_scenario", new { name = "Bear", parallelBp = 50, steepenerBp = 0 }),
            Says("Nothing was saved."));
        var snapshotId = await SnapshotWithTradeAsync(client, "C-3");

        var answer = await AskAsync(client, snapshotId, "Save a 50bp scenario called Bear.");

        Assert.Equal(new CopilotToolCallDto("save_scenario", "Denied"), Assert.Single(answer.ToolCalls));
        Assert.Equal("Answered", answer.Status);
    }

    [Fact]
    public async Task Without_a_configured_provider_the_endpoint_says_so_with_a_503()
    {
        var client = factory.CreateClient();
        var snapshotId = await SnapshotWithTradeAsync(client, "C-4");

        var response = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(snapshotId, "Hello?"), Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.EndsWith("/unavailable", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
        Assert.Contains("ANTHROPIC_API_KEY is not set", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_question_is_required_and_an_unknown_snapshot_is_a_404(string question)
    {
        var client = ClientWith(Says("Unused."));

        var blank = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(Guid.NewGuid(), question), Ct);
        var tooLong = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(Guid.NewGuid(), new string('x', 2001)), Ct);
        var noSnapshot = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(Guid.NewGuid(), "Hello?"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noSnapshot.StatusCode);
    }

    private static async Task<Guid> SnapshotWithTradeAsync(HttpClient client, string tradeId)
    {
        var snapshot = await ApiFactory.CreateSnapshotAsync(client, Ct);
        (await client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade(tradeId), Ct)).EnsureSuccessStatusCode();
        return snapshot.Id;
    }

    private static async Task<CopilotAnswerResponse> AskAsync(HttpClient client, Guid snapshotId, string question)
    {
        var response = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(snapshotId, question), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CopilotAnswerResponse>(Ct))!;
    }

    private HttpClient ClientWith(params ModelResponse[] script) => factory
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IModelClient>();
            services.AddSingleton<IModelClient>(new Scripted(script));
        }))
        .CreateClient();

    private static ModelResponse Says(string text) => new([new TextPart(text)], StopReason.EndTurn, Usage, "scripted");

    private static ModelResponse Calls(string tool, object arguments) =>
        new([new ToolCallPart("call_0", tool, JsonSerializer.SerializeToElement(arguments))], StopReason.ToolUse, Usage, "scripted");

    private sealed class Scripted(ModelResponse[] script) : IModelClient
    {
        private int _next;

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(script[Interlocked.Increment(ref _next) - 1]);
    }
}
