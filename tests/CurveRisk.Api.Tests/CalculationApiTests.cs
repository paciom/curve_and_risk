using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CurveRisk.Api.Services;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

public sealed class CalculationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> SnapshotWithTradeAsync(string tradeId)
    {
        var snapshot = await ApiFactory.CreateSnapshotAsync(_client, Ct);
        (await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade(tradeId), Ct)).EnsureSuccessStatusCode();
        return snapshot.Id;
    }

    [Fact]
    public async Task A_valuation_over_http_is_the_figure_the_pricing_library_gives()
    {
        var snapshotId = await SnapshotWithTradeAsync("V-1");

        var response = await _client.PostAsJsonAsync("/api/v1/valuations", new ValuationRequest(snapshotId, "V-1"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new ValuationResponse("V-1", "USD", 1_270_062.29, 3.78, 3.5),
            await response.Content.ReadFromJsonAsync<ValuationResponse>(Ct));
    }

    [Fact]
    public async Task A_scenario_reprices_the_trade_on_the_shocked_curve()
    {
        var snapshotId = await SnapshotWithTradeAsync("S-1");

        var response = await _client.PostAsJsonAsync("/api/v1/scenario-runs", new ScenarioRunRequest(snapshotId, "S-1", 50, 0), Ct);
        var result = await response.Content.ReadFromJsonAsync<ScenarioRunResponse>(Ct);

        Assert.Equal(new ScenarioRunResponse("S-1", "USD", 50, 0, 1_270_062.29, 3_546_530.50, 2_276_468.21), result);
    }

    [Fact]
    public async Task Unknown_snapshots_and_trades_are_404_and_an_out_of_range_shock_is_422()
    {
        var snapshotId = await SnapshotWithTradeAsync("E-1");

        var noSnapshot = await _client.PostAsJsonAsync("/api/v1/valuations", new ValuationRequest(Guid.NewGuid(), "E-1"), Ct);
        var noTrade = await _client.PostAsJsonAsync("/api/v1/valuations", new ValuationRequest(snapshotId, "E-404"), Ct);
        var tooBig = await _client.PostAsJsonAsync("/api/v1/scenario-runs", new ScenarioRunRequest(snapshotId, "E-1", 5000, 0), Ct);
        var problem = await tooBig.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.NotFound, noSnapshot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noTrade.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooBig.StatusCode);
        Assert.EndsWith("/unprocessable", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_risk_run_is_accepted_at_once_and_completes_in_the_background()
    {
        var snapshotId = await SnapshotWithTradeAsync("R-1");

        var accepted = await _client.PostAsJsonAsync("/api/v1/risk-runs", new CreateRiskRunRequest(snapshotId, ["R-1"]), Ct);
        var pending = await accepted.Content.ReadFromJsonAsync<RiskRunResponse>(Ct);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal($"/api/v1/risk-runs/{pending!.Id}", accepted.Headers.Location!.OriginalString);
        Assert.Equal((RiskRunStatus.Pending, null, null), (pending.Status, pending.Results, pending.CompletedUtc));

        var done = await PollAsync(accepted.Headers.Location);

        Assert.Equal(RiskRunStatus.Completed, done.Status);
        Assert.NotNull(done.CompletedUtc);
        var risk = Assert.Single(done.Results!);
        Assert.Equal(("R-1", "USD", 46_075.02), (risk.TradeId, risk.Currency, risk.ParallelDv01));
        Assert.Equal(new BucketDeltaDto(5, 43_710.40), risk.Buckets[3]);
    }

    [Fact]
    public async Task A_risk_run_is_refused_up_front_for_bad_input()
    {
        var snapshotId = await SnapshotWithTradeAsync("R-2");

        var empty = await _client.PostAsJsonAsync("/api/v1/risk-runs", new CreateRiskRunRequest(snapshotId, []), Ct);
        var noTrade = await _client.PostAsJsonAsync("/api/v1/risk-runs", new CreateRiskRunRequest(snapshotId, ["R-404"]), Ct);
        var noSnapshot = await _client.PostAsJsonAsync("/api/v1/risk-runs", new CreateRiskRunRequest(Guid.NewGuid(), ["R-2"]), Ct);
        var noRun = await _client.GetAsync($"/api/v1/risk-runs/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noTrade.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noSnapshot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noRun.StatusCode);
    }

    private async Task<RiskRunResponse> PollAsync(Uri location)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var run = await _client.GetFromJsonAsync<RiskRunResponse>(location, Ct);
            if (run!.Status != RiskRunStatus.Pending)
            {
                return run;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("The risk run did not complete within five seconds.");
    }
}
