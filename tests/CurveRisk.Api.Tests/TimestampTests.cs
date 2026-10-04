using System.Net.Http.Json;
using CurveRisk.Api.Services;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

public sealed class TimestampTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Stored_instants_are_cut_to_the_millisecond_so_every_database_returns_them_unchanged()
    {
        var instant = new DateTimeOffset(2026, 10, 4, 9, 30, 15, TimeSpan.Zero).AddTicks(1_234_567);

        var stored = new FixedClock(instant).UtcNowToMillisecond();

        Assert.Equal(new DateTimeOffset(2026, 10, 4, 9, 30, 15, 123, TimeSpan.Zero), stored);
        Assert.Equal(0, stored.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public async Task What_a_create_returns_is_exactly_what_a_later_read_returns()
    {
        var client = factory.CreateClient();

        var posted = await client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("TS-1"), Ct);
        var created = await posted.Content.ReadFromJsonAsync<TradeResponse>(Ct);
        var fetched = await client.GetFromJsonAsync<TradeResponse>("/api/v1/trades/TS-1", Ct);
        var snapshot = await ApiFactory.CreateSnapshotAsync(client, Ct);
        var snapshotAgain = await client.GetFromJsonAsync<MarketSnapshotResponse>($"/api/v1/market-snapshots/{snapshot.Id}", Ct);

        Assert.Equal(created!.CreatedUtc, fetched!.CreatedUtc);
        Assert.Equal(0, created.CreatedUtc.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(snapshot.CreatedUtc, snapshotAgain!.CreatedUtc);
        Assert.Equal(0, snapshot.CreatedUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
