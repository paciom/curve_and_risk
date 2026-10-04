using System.Net;
using System.Net.Http.Json;
using System.Text;
using CurveRisk.Contracts.V1;
using CurveRisk.Copilot;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CurveRisk.Api.Tests;

/// <summary>
/// The error contract: each caller mistake has one status, one stable problem type and a detail that
/// says what to do next. Clients branch on the type and show the detail, so both are asserted exactly.
/// </summary>
public sealed class ProblemDetailsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> DeleteAsync(string tradeId, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/trades/{tradeId}");
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(request, Ct);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_change_without_if_match_says_which_header_to_send()
    {
        await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PD-428"), Ct);

        var response = await DeleteAsync("PD-428", ifMatch: null);
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.Equal(ProblemReader.TypePrefix + "precondition-required", problem.Text("type"));
        Assert.Equal("Send the If-Match header with the ETag of the version you are changing.", problem.Text("detail"));
    }

    [Fact]
    public async Task A_change_with_a_stale_etag_says_to_fetch_and_retry()
    {
        await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PD-412"), Ct);

        var response = await DeleteAsync("PD-412", "\"v9\"");
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(ProblemReader.TypePrefix + "precondition-failed", problem.Text("type"));
        Assert.Equal("The resource has changed since it was read. Fetch it again and retry.", problem.Text("detail"));
    }

    [Fact]
    public async Task A_duplicate_trade_id_is_a_conflict_that_names_the_trade()
    {
        await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PD-409"), Ct);

        var response = await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PD-409"), Ct);
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Trade 'PD-409' already exists.", problem.Text("detail"));
    }

    [Fact]
    public async Task A_missing_resource_is_named_by_kind_and_id()
    {
        var id = new Guid("0199a000-0000-7000-8000-000000000001");
        var snapshot = await ApiFactory.CreateSnapshotAsync(_client, Ct);

        var trade = await _client.GetAsync("/api/v1/trades/PD-NOPE", Ct);
        var market = await _client.GetAsync($"/api/v1/market-snapshots/{id}", Ct);
        var run = await _client.GetAsync($"/api/v1/risk-runs/{id}", Ct);
        var priced = await _client.PostAsJsonAsync("/api/v1/valuations", new ValuationRequest(snapshot.Id, "PD-UNBOOKED"), Ct);

        Assert.Equal("Trade 'PD-NOPE' was not found.", (await trade.ProblemAsync(Ct)).Text("detail"));
        Assert.Equal("Market snapshot '0199a000-0000-7000-8000-000000000001' was not found.", (await market.ProblemAsync(Ct)).Text("detail"));
        Assert.Equal("Risk run '0199a000-0000-7000-8000-000000000001' was not found.", (await run.ProblemAsync(Ct)).Text("detail"));
        Assert.Equal("Trade 'PD-UNBOOKED' was not found.", (await priced.ProblemAsync(Ct)).Text("detail"));
    }

    [Fact]
    public async Task A_route_that_does_not_exist_is_still_answered_with_a_problem_document()
    {
        var response = await _client.GetAsync("/api/v1/no-such-resource", Ct);
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task A_risk_run_without_trades_says_one_is_required(string tradeIds)
    {
        var body = $$"""{ "snapshotId": "0199a000-0000-7000-8000-000000000001", "tradeIds": {{tradeIds}} }""";

        var response = await _client.PostAsync("/api/v1/risk-runs", Json(body), Ct);
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["At least one trade id is required."], problem.ErrorsFor("tradeIds"));
    }

    [Fact]
    public async Task A_question_may_be_2000_characters_but_not_2001()
    {
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IModelClient>();
                services.AddSingleton<IModelClient>(new OneAnswer());
            }))
            .CreateClient();
        var snapshot = await ApiFactory.CreateSnapshotAsync(client, Ct);

        var longest = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(snapshot.Id, new string('q', 2000)), Ct);
        var tooLong = await client.PostAsJsonAsync("/api/v1/copilot/answers", new CopilotQuestionRequest(snapshot.Id, new string('q', 2001)), Ct);
        var problem = await tooLong.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(["A question of up to 2000 characters is required."], problem.ErrorsFor("question"));
    }

    /// <summary>A model that always gives the same short answer, so no test here can reach a real provider.</summary>
    private sealed class OneAnswer : IModelClient
    {
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelResponse([new TextPart("Understood.")], StopReason.EndTurn, new TokenUsage(1, 1, 0, 0), "scripted"));
    }
}
