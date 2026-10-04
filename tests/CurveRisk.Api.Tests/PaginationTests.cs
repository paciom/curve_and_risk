using System.Net.Http.Json;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

/// <summary>
/// The edges of a listing. This class owns its database, so it knows exactly how many rows exist:
/// a next cursor is offered only when there is something after the page.
/// </summary>
public sealed class PaginationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_page_of_trades_that_reaches_the_end_offers_no_next_cursor()
    {
        await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PG-1"), Ct);
        await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PG-2"), Ct);

        var beforeTheEnd = await _client.GetFromJsonAsync<PageResponse<TradeResponse>>("/api/v1/trades?pageSize=1", Ct);
        var exactlyFull = await _client.GetFromJsonAsync<PageResponse<TradeResponse>>("/api/v1/trades?pageSize=2", Ct);
        var pastTheEnd = await _client.GetFromJsonAsync<PageResponse<TradeResponse>>("/api/v1/trades?pageSize=2&cursor=PG-2", Ct);

        Assert.Equal("PG-1", beforeTheEnd!.NextCursor);
        Assert.Equal(["PG-1", "PG-2"], exactlyFull!.Items.Select(trade => trade.TradeId));
        Assert.Null(exactlyFull.NextCursor);
        Assert.Empty(pastTheEnd!.Items);
        Assert.Null(pastTheEnd.NextCursor);
    }

    [Fact]
    public async Task A_page_of_snapshots_that_reaches_the_end_offers_no_next_cursor()
    {
        var first = await ApiFactory.CreateSnapshotAsync(_client, Ct);
        var second = await ApiFactory.CreateSnapshotAsync(_client, Ct);

        var beforeTheEnd = await _client.GetFromJsonAsync<PageResponse<MarketSnapshotResponse>>("/api/v1/market-snapshots?pageSize=1", Ct);
        var exactlyFull = await _client.GetFromJsonAsync<PageResponse<MarketSnapshotResponse>>("/api/v1/market-snapshots?pageSize=2", Ct);
        var byDefault = await _client.GetFromJsonAsync<PageResponse<MarketSnapshotResponse>>("/api/v1/market-snapshots", Ct);

        // Two snapshots made in the same millisecond have no defined order, so order is not asserted here.
        Assert.Equal(Assert.Single(beforeTheEnd!.Items).Id.ToString(), beforeTheEnd.NextCursor);
        Assert.Equal(2, exactlyFull!.Items.Count);
        Assert.Null(exactlyFull.NextCursor);
        Assert.Contains(byDefault!.Items, snapshot => snapshot.Id == first.Id);
        Assert.Contains(byDefault.Items, snapshot => snapshot.Id == second.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_page_size_below_one_is_treated_as_one(int pageSize)
    {
        await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PG-1"), Ct);
        await _client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("PG-2"), Ct);

        var page = await _client.GetFromJsonAsync<PageResponse<TradeResponse>>($"/api/v1/trades?pageSize={pageSize}", Ct);

        Assert.Equal("PG-1", Assert.Single(page!.Items).TradeId);
    }
}
