using CurveRisk.Ai.Tools;
using CurveRisk.Engine;

namespace CurveRisk.Analytics.Tests;

/// <summary>What the engine reports and refuses at its boundary: units, limits, identifiers and messages.</summary>
public class EngineBoundaryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly AnalyticsRiskEngine _engine = AnalyticsRiskEngine.CreateDemo();

    [Fact]
    public async Task Trades_are_listed_as_swaps_with_direction_and_the_fixed_rate_in_percent()
    {
        var trades = await _engine.ListTradesAsync(Ct);

        Assert.Equal(3, trades.Count);
        Assert.Equal(
            new TradeSummary("T-1001", "InterestRateSwap", "PayFixed", 100_000_000, 3.5, 5, "USD-SOFR", "Client hedge, pay-fixed 5Y"),
            trades[0]);
        Assert.Equal(
            new TradeSummary("T-1002", "InterestRateSwap", "ReceiveFixed", 50_000_000, 4.25, 10, "USD-SOFR", "Receive-fixed 10Y asset swap overlay"),
            trades[1]);
    }

    [Fact]
    public async Task An_unknown_curve_is_refused_with_the_curves_that_exist()
    {
        var ex = await Assert.ThrowsAsync<RiskEngineException>(() => _engine.GetCurveAsync("EUR-ESTR", Ct));

        Assert.Equal("Unknown curve 'EUR-ESTR'. Available curves: USD-SOFR.", ex.Message);
    }

    [Fact]
    public async Task An_unknown_trade_is_refused_with_the_trades_that_exist()
    {
        var ex = await Assert.ThrowsAsync<RiskEngineException>(() => _engine.PriceTradeAsync("T-9999", Ct));

        Assert.Equal("Unknown trade 'T-9999'. Known trades: T-1001, T-1002, T-1003.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_scenario_without_a_name_is_refused_and_not_saved(string name)
    {
        var ex = await Assert.ThrowsAsync<RiskEngineException>(() => _engine.SaveScenarioAsync(name, new ScenarioShock(25, 0), Ct));

        Assert.Equal("Scenario name must not be empty.", ex.Message);
        Assert.Empty(_engine.SavedScenarios);
    }

    [Fact]
    public async Task Saved_scenarios_are_numbered_from_one_with_four_digits_and_keep_the_trimmed_name()
    {
        var first = await _engine.SaveScenarioAsync("  Bear steepener ", new ScenarioShock(50, 20), Ct);
        var second = await _engine.SaveScenarioAsync("Rally", new ScenarioShock(-25, 0), Ct);

        Assert.Equal(new SavedScenario("S-0001", "Bear steepener", new ScenarioShock(50, 20)), first);
        Assert.Equal("S-0002", second.ScenarioId);
        Assert.Equal(2, _engine.SavedScenarios.Count);
    }

    [Theory]
    [InlineData(1000, 0)]
    [InlineData(-1000, 0)]
    [InlineData(0, 1000)]
    [InlineData(0, -1000)]
    public async Task A_shock_of_exactly_1000bp_is_the_largest_accepted(double parallelBp, double steepenerBp)
    {
        var shock = new ScenarioShock(parallelBp, steepenerBp);

        var result = await _engine.RunScenarioAsync("T-1001", shock, Ct);
        var saved = await _engine.SaveScenarioAsync("Limit", shock, Ct);

        Assert.Equal(shock, result.Shock);
        Assert.Equal(shock, saved.Shock);
    }

    [Theory]
    [InlineData(1000.5, 0)]
    [InlineData(0, -1000.5)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public async Task A_shock_beyond_1000bp_or_not_finite_is_refused_when_run_and_when_saved(double parallelBp, double steepenerBp)
    {
        var shock = new ScenarioShock(parallelBp, steepenerBp);

        var run = await Assert.ThrowsAsync<RiskEngineException>(() => _engine.RunScenarioAsync("T-1001", shock, Ct));
        var save = await Assert.ThrowsAsync<RiskEngineException>(() => _engine.SaveScenarioAsync("Too big", shock, Ct));

        Assert.Equal("Shocks must be finite and within +/-1000bp.", run.Message);
        Assert.Equal("Shocks must be finite and within +/-1000bp.", save.Message);
        Assert.Empty(_engine.SavedScenarios);
    }
}
