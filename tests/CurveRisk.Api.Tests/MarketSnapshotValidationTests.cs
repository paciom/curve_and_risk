using System.Net;
using System.Net.Http.Json;
using System.Text;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

/// <summary>What a market snapshot may contain, and the message a caller gets for each rule it breaks.</summary>
public sealed class MarketSnapshotValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Each_invalid_field_has_a_message_that_says_what_to_send()
    {
        var request = new CreateMarketSnapshotRequest(" ", "30/09/2026", "Spline", [new QuoteDto("10Y", 4.02), new QuoteDto("5 years", 3.78)]);

        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", request, Ct);
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A curve id is required."], problem.ErrorsFor("curveId"));
        Assert.Equal(["Use the format yyyy-MM-dd."], problem.ErrorsFor("asOf"));
        Assert.Equal(["Use one of: LogLinearDiscount, LinearZero, MonotoneCubicLogDiscount."], problem.ErrorsFor("interpolation"));
        Assert.Equal(["Use a positive count and a unit, such as 6M or 10Y."], problem.ErrorsFor("quotes[1].tenor"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task A_snapshot_with_no_quotes_says_one_is_required(string quotes)
    {
        var body = $$"""{ "curveId": "USD-SOFR", "asOf": "2026-09-30", "interpolation": "LinearZero", "quotes": {{quotes}} }""";

        var response = await _client.PostAsync("/api/v1/market-snapshots", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        var problem = await response.ProblemAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["At least one quote is required."], problem.ErrorsFor("quotes"));
    }

    [Fact]
    public async Task An_interpolation_name_must_match_exactly_including_case()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", ApiFactory.DemoSnapshot with { Interpolation = "linearzero" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_surrounding_space_in_a_curve_id_is_not_stored()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", ApiFactory.DemoSnapshot with { CurveId = " USD-SOFR " }, Ct);
        var created = await response.Content.ReadFromJsonAsync<MarketSnapshotResponse>(Ct);

        var curve = await _client.GetFromJsonAsync<CurveResponse>($"/api/v1/market-snapshots/{created!.Id}/curve", Ct);

        Assert.Equal("USD-SOFR", curve!.CurveId);
    }
}
