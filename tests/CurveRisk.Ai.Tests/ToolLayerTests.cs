using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Engine;

namespace CurveRisk.Ai.Tests;

public class ToolLayerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Catalog_is_stable_and_only_save_scenario_needs_approval()
    {
        var tools = ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo());

        // Order matters: the tool list is part of the cached prompt prefix.
        Assert.Equal(
            ["list_trades", "get_curve", "price_trade", "run_risk", "run_scenario", "save_scenario"],
            tools.Select(t => t.Name));
        Assert.Equal(["save_scenario"], tools.Where(t => t.RequiresApproval).Select(t => t.Name));
    }

    [Fact]
    public void Every_tool_has_a_description_and_an_object_schema_without_infrastructure_parameters()
    {
        foreach (var tool in ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()))
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Function.Description));
            Assert.Equal("object", tool.Function.JsonSchema.GetProperty("type").GetString());
            Assert.DoesNotContain("cancellationToken", tool.Function.JsonSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Invoke_binds_json_arguments_and_returns_json()
    {
        var tool = ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()).Single(t => t.Name == ToolCatalog.RunScenario);
        var args = JsonSerializer.SerializeToElement(new { tradeId = "T-1001", parallelBp = 50, steepenerBp = 0 });

        using var result = JsonDocument.Parse(await ToolCatalog.InvokeAsync(tool, args, Ct));

        Assert.Equal(50, result.RootElement.GetProperty("shock").GetProperty("parallelBp").GetDouble());
        Assert.True(result.RootElement.GetProperty("profitAndLoss").GetDouble() > 0, "pay-fixed gains when rates rise");
    }

    [Fact]
    public async Task Swap_pv_has_the_sign_of_par_minus_fixed_for_a_payer()
    {
        var engine = AnalyticsRiskEngine.CreateDemo();

        foreach (var trade in await engine.ListTradesAsync(Ct))
        {
            var valuation = await engine.PriceTradeAsync(trade.TradeId, Ct);
            var payerSign = Math.Sign(valuation.ParRatePercent - valuation.FixedRatePercent);
            var expected = trade.Direction == "PayFixed" ? payerSign : -payerSign;
            Assert.Equal(expected, Math.Sign(valuation.PresentValue));
        }
    }

    [Fact]
    public async Task Bucket_deltas_add_up_to_parallel_dv01()
    {
        var engine = AnalyticsRiskEngine.CreateDemo();

        foreach (var trade in await engine.ListTradesAsync(Ct))
        {
            var risk = await engine.RunRiskAsync(trade.TradeId, Ct);

            // Linear interpolation means the bumps partition a parallel shift; only convexity and rounding remain.
            Assert.Equal(risk.ParallelDv01, risk.Buckets.Sum(b => b.Delta), tolerance: Math.Abs(risk.ParallelDv01) * 1e-3);
        }
    }

    [Fact]
    public async Task Scenario_is_consistent_with_dv01_and_zero_shock_is_flat()
    {
        var engine = AnalyticsRiskEngine.CreateDemo();
        var risk = await engine.RunRiskAsync("T-1002", Ct);

        var flat = await engine.RunScenarioAsync("T-1002", new ScenarioShock(0, 0), Ct);
        var up10 = await engine.RunScenarioAsync("T-1002", new ScenarioShock(10, 0), Ct);

        Assert.Equal(0, flat.ProfitAndLoss);
        Assert.Equal(risk.ParallelDv01 * 10, up10.ProfitAndLoss, tolerance: Math.Abs(risk.ParallelDv01 * 10) * 0.02);
    }

    [Fact]
    public async Task Largest_bucket_of_the_ten_year_swap_is_the_ten_year_pillar()
    {
        // The eval dataset refers to this bucket by index (buckets.5); keep the two in step.
        var risk = await AnalyticsRiskEngine.CreateDemo().RunRiskAsync("T-1002", Ct);

        var largest = risk.Buckets.MaxBy(b => Math.Abs(b.Delta))!;

        Assert.Equal(10, largest.TenorYears);
        Assert.Same(largest, risk.Buckets[5]);
    }

    [Fact]
    public async Task Unknown_ids_and_out_of_range_shocks_fail_with_messages_a_model_can_act_on()
    {
        var engine = AnalyticsRiskEngine.CreateDemo();

        var unknown = await Assert.ThrowsAsync<RiskEngineException>(() => engine.PriceTradeAsync("T-9999", Ct));
        Assert.Contains("T-1001", unknown.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<RiskEngineException>(() => engine.RunScenarioAsync("T-1001", new ScenarioShock(5000, 0), Ct));
        await Assert.ThrowsAsync<RiskEngineException>(() => engine.RunScenarioAsync("T-1001", new ScenarioShock(double.NaN, 0), Ct));
    }
}
