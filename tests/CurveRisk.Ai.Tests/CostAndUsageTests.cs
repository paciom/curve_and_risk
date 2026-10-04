using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

public class CostAndUsageTests
{
    [Fact]
    public void Adding_usage_adds_every_kind_of_token()
    {
        var total = new TokenUsage(1000, 100, 700, 40) + new TokenUsage(200, 20, 50, 5);

        Assert.Equal(new TokenUsage(1200, 120, 750, 45), total);
    }

    [Fact]
    public void Cost_prices_input_output_cache_reads_and_cache_writes_per_million_tokens()
    {
        var usage = new TokenUsage(InputTokens: 1_000_000, OutputTokens: 500_000, CacheReadTokens: 2_000_000, CacheWriteTokens: 100_000);

        var cost = ModelPricing.ClaudeOpus55.CostOf(usage);

        // 4.00 input + 10.00 output + 0.40 cache reads + 0.50 cache writes.
        Assert.Equal(14.90m, cost);
    }
}
