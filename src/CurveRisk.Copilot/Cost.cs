namespace CurveRisk.Copilot;

/// <summary>USD per million tokens.</summary>
public sealed record ModelPricing(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
{
    /// <summary>
    /// Claude Opus 5.5 list prices at the time of writing. Cache writes are assumed at 1.25x input
    /// (the 5-minute TTL rate); override through <see cref="CopilotOptions.Pricing"/> if that changes.
    /// </summary>
    public static ModelPricing ClaudeOpus55 { get; } = new(Input: 4.00m, Output: 20.00m, CacheRead: 0.20m, CacheWrite: 5.00m);

    /// <summary>For providers whose prices are not known here: cost is reported as zero, not guessed.</summary>
    public static ModelPricing Unknown { get; } = new(0m, 0m, 0m, 0m);

    public decimal CostOf(TokenUsage usage) =>
        ((usage.InputTokens * Input)
         + (usage.OutputTokens * Output)
         + (usage.CacheReadTokens * CacheRead)
         + (usage.CacheWriteTokens * CacheWrite)) / 1_000_000m;
}

/// <summary>
/// Process-wide daily spend cap for a public demo. Checked before each model call, so the overshoot is
/// bounded by the calls already in flight when the cap is reached: one per concurrent question.
/// </summary>
public sealed class BudgetGuard(decimal dailyLimitUsd, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private DateOnly _day;
    private decimal _spent;

    public static BudgetGuard Unlimited => new(decimal.MaxValue);

    public decimal SpentTodayUsd
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return _spent;
            }
        }
    }

    public bool HasBudget => SpentTodayUsd < dailyLimitUsd;

    public void Record(decimal costUsd)
    {
        lock (_gate)
        {
            Roll();
            _spent += costUsd;
        }
    }

    private void Roll()
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        if (today != _day)
        {
            _day = today;
            _spent = 0;
        }
    }
}
