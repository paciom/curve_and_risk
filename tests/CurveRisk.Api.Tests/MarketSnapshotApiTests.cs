using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

public sealed class MarketSnapshotApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Creating_a_snapshot_returns_201_with_its_location_and_it_can_be_read_back()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", ApiFactory.DemoSnapshot, Ct);
        var created = await response.Content.ReadFromJsonAsync<MarketSnapshotResponse>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/market-snapshots/{created!.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(("USD-SOFR", "2026-09-30", "LogLinearDiscount", 8), (created.CurveId, created.AsOf, created.Interpolation, created.Quotes.Count));

        var fetched = await _client.GetFromJsonAsync<MarketSnapshotResponse>(response.Headers.Location, Ct);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(new QuoteDto("5Y", 3.78), fetched.Quotes[3]);
    }

    [Fact]
    public async Task The_curve_of_a_snapshot_is_calibrated_to_its_quotes()
    {
        var snapshot = await ApiFactory.CreateSnapshotAsync(_client, Ct);

        var curve = await _client.GetFromJsonAsync<CurveResponse>($"/api/v1/market-snapshots/{snapshot.Id}/curve", Ct);

        Assert.Equal(snapshot.Id, curve!.SnapshotId);
        Assert.Equal(8, curve.Pillars.Count);
        Assert.Equal(new CurvePillarDto(1, 4.0237, 0.960138), curve.Pillars[0]);
    }

    [Fact]
    public async Task Snapshots_are_listed_in_creation_order_one_page_at_a_time()
    {
        var created = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            created.Add((await ApiFactory.CreateSnapshotAsync(_client, Ct)).Id);
        }

        var first = await _client.GetFromJsonAsync<PageResponse<MarketSnapshotResponse>>($"/api/v1/market-snapshots?pageSize=2&cursor={Guid.Empty}", Ct);
        var all = new List<Guid>(first!.Items.Select(item => item.Id));
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = await _client.GetFromJsonAsync<PageResponse<MarketSnapshotResponse>>($"/api/v1/market-snapshots?pageSize=2&cursor={cursor}", Ct);
            all.AddRange(page!.Items.Select(item => item.Id));
            cursor = page.NextCursor;
        }

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(created, all.Where(created.Contains));
    }

    [Fact]
    public async Task Invalid_fields_are_reported_together_as_a_validation_problem()
    {
        var request = new CreateMarketSnapshotRequest(" ", "30/09/2026", "Spline", [new QuoteDto("5 years", 3.78), new QuoteDto("10Y", 4.02)]);

        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", request, Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith("/validation", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
        var errors = problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["asOf", "curveId", "interpolation", "quotes[0].tenor"], errors);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("")]
    public async Task An_interpolation_that_is_not_a_named_scheme_is_rejected(string interpolation)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", ApiFactory.DemoSnapshot with { Interpolation = interpolation }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_snapshot_without_quotes_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", ApiFactory.DemoSnapshot with { Quotes = [] }, Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("quotes", out _));
    }

    [Fact]
    public async Task Quotes_that_cannot_make_a_curve_are_rejected_as_unprocessable_when_the_snapshot_is_created()
    {
        var request = ApiFactory.DemoSnapshot with { Quotes = [new QuoteDto("1Y", -90)] };

        var response = await _client.PostAsJsonAsync("/api/v1/market-snapshots", request, Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("OIS 1Y", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_snapshot_is_a_404_problem_for_both_the_snapshot_and_its_curve()
    {
        var id = Guid.NewGuid();

        var snapshot = await _client.GetAsync($"/api/v1/market-snapshots/{id}", Ct);
        var curve = await _client.GetAsync($"/api/v1/market-snapshots/{id}/curve", Ct);
        var problem = await snapshot.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.NotFound, snapshot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, curve.StatusCode);
        Assert.EndsWith("/not-found", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
        Assert.Contains(id.ToString(), problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_service_describes_itself_and_reports_health()
    {
        var openApi = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var health = await _client.GetFromJsonAsync<JsonElement>("/health", Ct);

        var paths = openApi.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();
        Assert.Contains("/api/v1/market-snapshots", paths);
        Assert.Contains("/api/v1/trades/{tradeId}", paths);
        Assert.Contains("/api/v1/risk-runs/{id}", paths);
        Assert.DoesNotContain("/health", paths);
        Assert.Equal("ok", health.GetProperty("status").GetString());
    }
}
