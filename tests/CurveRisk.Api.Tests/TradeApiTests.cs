using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

public sealed class TradeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> PostAsync(CreateTradeRequest trade, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/trades") { Content = JsonContent.Create(trade) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string tradeId, string? ifMatch, UpdateTradeRequest? body = null)
    {
        var request = new HttpRequestMessage(method, $"/api/v1/trades/{tradeId}");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return _client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Booking_a_trade_returns_201_with_location_and_etag_and_it_can_be_read_back()
    {
        var response = await PostAsync(ApiFactory.Trade("T-BOOK"));
        var created = await response.Content.ReadFromJsonAsync<TradeResponse>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/v1/trades/T-BOOK", response.Headers.Location!.OriginalString);
        Assert.Equal("\"v1\"", response.Headers.ETag!.Tag);
        Assert.Equal(("T-BOOK", 100_000_000m, 3.5, "PayFixed", "5Y", "Client hedge"),
            (created!.TradeId, created.NotionalAmount, created.FixedRatePercent, created.Direction, created.Tenor, created.Description));

        var fetched = await _client.GetAsync("/api/v1/trades/T-BOOK", Ct);
        Assert.Equal("\"v1\"", fetched.Headers.ETag!.Tag);
        Assert.Equal(created, await fetched.Content.ReadFromJsonAsync<TradeResponse>(Ct));
    }

    [Fact]
    public async Task Retrying_a_post_with_the_same_idempotency_key_returns_the_original_and_creates_nothing()
    {
        var first = await PostAsync(ApiFactory.Trade("T-RETRY"), "key-1");
        var retry = await PostAsync(ApiFactory.Trade("T-RETRY"), "key-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<TradeResponse>(Ct), await retry.Content.ReadFromJsonAsync<TradeResponse>(Ct));
    }

    [Theory]
    [InlineData("key-a", "key-b")]
    [InlineData("key-a", null)]
    [InlineData(null, null)]
    public async Task Posting_an_existing_trade_id_without_the_original_key_is_a_conflict(string? firstKey, string? secondKey)
    {
        var id = $"T-DUP-{firstKey}-{secondKey}";
        await PostAsync(ApiFactory.Trade(id), firstKey);

        var second = await PostAsync(ApiFactory.Trade(id), secondKey);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.EndsWith("/conflict", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_invalid_field_is_reported()
    {
        var bad = new CreateTradeRequest(" ", 0m, 250, "Sideways", "five", null);

        var response = await PostAsync(bad);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["direction", "fixedRatePercent", "notionalAmount", "tenor", "tradeId"], errors);
    }

    [Fact]
    public async Task Updating_needs_the_current_etag_and_returns_a_new_one()
    {
        await PostAsync(ApiFactory.Trade("T-EDIT"));
        var change = new UpdateTradeRequest(250_000_000m, 3.95, "2y", "Resized");

        var missing = await SendAsync(HttpMethod.Put, "T-EDIT", ifMatch: null, change);
        var stale = await SendAsync(HttpMethod.Put, "T-EDIT", "\"v7\"", change);
        var updated = await SendAsync(HttpMethod.Put, "T-EDIT", "\"v1\"", change);
        var replayed = await SendAsync(HttpMethod.Put, "T-EDIT", "\"v1\"", change);

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("\"v2\"", updated.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, replayed.StatusCode);

        var trade = await updated.Content.ReadFromJsonAsync<TradeResponse>(Ct);
        Assert.Equal((250_000_000m, 3.95, "2Y", "Resized", "PayFixed"),
            (trade!.NotionalAmount, trade.FixedRatePercent, trade.Tenor, trade.Description, trade.Direction));
    }

    [Fact]
    public async Task An_update_is_validated_and_an_unknown_trade_is_a_404()
    {
        await PostAsync(ApiFactory.Trade("T-BADEDIT"));

        var invalid = await SendAsync(HttpMethod.Put, "T-BADEDIT", "\"v1\"", new UpdateTradeRequest(-1m, 3.5, "5Y", null));
        var unknown = await SendAsync(HttpMethod.Put, "T-NOPE", "\"v1\"", new UpdateTradeRequest(1m, 3.5, "5Y", null));
        var unknownGet = await _client.GetAsync("/api/v1/trades/T-NOPE", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownGet.StatusCode);
    }

    [Fact]
    public async Task Deleting_needs_the_current_etag_and_then_the_trade_is_gone()
    {
        await PostAsync(ApiFactory.Trade("T-GONE"));

        var missing = await SendAsync(HttpMethod.Delete, "T-GONE", ifMatch: null);
        var stale = await SendAsync(HttpMethod.Delete, "T-GONE", "\"v9\"");
        var deleted = await SendAsync(HttpMethod.Delete, "T-GONE", "\"v1\"");
        var again = await SendAsync(HttpMethod.Delete, "T-GONE", "\"v1\"");

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Trades_are_listed_by_id_one_page_at_a_time()
    {
        foreach (var id in new[] { "P-3", "P-1", "P-2" })
        {
            await PostAsync(ApiFactory.Trade(id));
        }

        var first = await _client.GetFromJsonAsync<PageResponse<TradeResponse>>("/api/v1/trades?pageSize=2&cursor=P-0", Ct);
        var second = await _client.GetFromJsonAsync<PageResponse<TradeResponse>>($"/api/v1/trades?pageSize=2&cursor={first!.NextCursor}", Ct);
        var everything = await _client.GetFromJsonAsync<PageResponse<TradeResponse>>("/api/v1/trades", Ct);

        Assert.Equal(["P-1", "P-2"], first.Items.Select(trade => trade.TradeId));
        Assert.Equal("P-2", first.NextCursor);
        Assert.Equal("P-3", second!.Items[0].TradeId);
        Assert.True(everything!.Items.Count >= 3);
    }
}
