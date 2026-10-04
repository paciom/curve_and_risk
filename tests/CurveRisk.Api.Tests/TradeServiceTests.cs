using CurveRisk.Api.Persistence;
using CurveRisk.Api.Services;
using CurveRisk.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CurveRisk.Api.Tests;

/// <summary>Trade behaviour that a single HTTP request cannot produce: a lost race, a capped book, a value JSON cannot carry.</summary>
public sealed class TradeServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static CreateTradeRequest Trade(string id) => ApiFactory.Trade(id);

    [Fact]
    public async Task A_writer_who_loses_a_race_is_told_the_trade_changed_and_the_winner_is_kept()
    {
        using var slow = factory.Services.CreateScope();
        using var fast = factory.Services.CreateScope();
        var slowTrades = slow.ServiceProvider.GetRequiredService<TradeService>();
        await slowTrades.CreateAsync(Trade("RACE-1"), idempotencyKey: null, Ct);

        // The slow writer has read version 1; the fast writer commits version 2 before the slow one saves.
        await slow.ServiceProvider.GetRequiredService<CurveRiskDbContext>().Trades.SingleAsync(trade => trade.TradeId == "RACE-1", Ct);
        await fast.ServiceProvider.GetRequiredService<TradeService>()
            .UpdateAsync("RACE-1", new UpdateTradeRequest(2m, 3.5, "5Y", "fast"), "\"v1\"", Ct);

        await Assert.ThrowsAsync<PreconditionFailedException>(
            () => slowTrades.UpdateAsync("RACE-1", new UpdateTradeRequest(3m, 3.5, "5Y", "slow"), "\"v1\"", Ct));

        using var reader = factory.Services.CreateScope();
        var stored = await reader.ServiceProvider.GetRequiredService<TradeService>().GetAsync("RACE-1", Ct);
        Assert.Equal((2m, "fast", "\"v2\""), (stored.Trade.NotionalAmount, stored.Trade.Description, stored.ETag));
    }

    [Fact]
    public async Task A_capped_book_is_the_first_trades_in_id_order()
    {
        using var scope = factory.Services.CreateScope();
        var trades = scope.ServiceProvider.GetRequiredService<TradeService>();

        // This class shares one database: the "0-" prefix sorts these before every other test's trades.
        await trades.CreateAsync(Trade("0-BOOK-3"), idempotencyKey: null, Ct);
        await trades.CreateAsync(Trade("0-BOOK-1"), idempotencyKey: null, Ct);
        await trades.CreateAsync(Trade("0-BOOK-2"), idempotencyKey: null, Ct);

        var book = await trades.GetBookAsync(limit: 2, Ct);

        Assert.Equal(["0-BOOK-1", "0-BOOK-2"], book.Select(trade => trade.TradeId));
    }

    [Fact]
    public async Task The_book_carries_the_fixed_rate_as_a_fraction_and_the_direction_as_booked()
    {
        using var scope = factory.Services.CreateScope();
        var trades = scope.ServiceProvider.GetRequiredService<TradeService>();
        await trades.CreateAsync(Trade("DEF-PAY"), idempotencyKey: null, Ct);
        await trades.CreateAsync(Trade("DEF-RECEIVE") with { Direction = "ReceiveFixed" }, idempotencyKey: null, Ct);

        var definitions = await trades.GetDefinitionsAsync(["DEF-PAY", "DEF-RECEIVE"], Ct);

        var pay = Assert.Single(definitions, trade => trade.TradeId == "DEF-PAY");
        var receive = Assert.Single(definitions, trade => trade.TradeId == "DEF-RECEIVE");
        Assert.Equal((100_000_000d, 0.035, true, "5Y", "Client hedge"),
            (pay.Terms.Notional, pay.Terms.FixedRate, pay.Terms.PayFixed, pay.Terms.Tenor.ToString(), pay.Description));
        Assert.False(receive.Terms.PayFixed);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task A_fixed_rate_that_is_not_a_finite_number_is_rejected(double ratePercent)
    {
        using var scope = factory.Services.CreateScope();
        var trades = scope.ServiceProvider.GetRequiredService<TradeService>();

        var rejected = await Assert.ThrowsAsync<RequestValidationException>(
            () => trades.CreateAsync(Trade("NOT-FINITE") with { FixedRatePercent = ratePercent }, idempotencyKey: null, Ct));

        Assert.Equal("fixedRatePercent", Assert.Single(rejected.Errors).Key);
    }
}
