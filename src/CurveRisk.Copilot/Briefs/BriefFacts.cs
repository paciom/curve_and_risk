using System.Text.Json;
using CurveRisk.Ai.Tools;

namespace CurveRisk.Copilot.Briefs;

/// <param name="ParallelDv01">PV change for a +1bp parallel shift of zero rates, in trade currency.</param>
public sealed record TradeLine(string TradeId, double PresentValue, double ParRatePercent, double ParallelDv01);

/// <param name="ProfitAndLoss">Sum over the book of each trade's shocked PV less its base PV, in trade currency.</param>
public sealed record ScenarioLine(string Name, double ParallelBp, double SteepenerBp, double ProfitAndLoss);

/// <summary>What the rules picked out as worth saying. Each is a selection from the figures, not a new figure.</summary>
public sealed record BriefFlags(TradeLine LargestDv01Trade, BucketDelta MostExposedBucket, ScenarioLine WorstScenario);

/// <summary>
/// The figures of a brief. Per-trade figures are the engine's own. Book totals are sums of engine
/// figures taken by this code, rounded to the engine's two decimals; the model is given them and
/// never computes one. All amounts are in <see cref="Currency"/>, rates in percent, shocks in bp.
/// </summary>
/// <param name="Buckets">Sum over the book of each trade's PV change for +1bp in that pillar's zero rate.</param>
public sealed record BriefFacts(
    string Currency,
    int TradeCount,
    double TotalPresentValue,
    double TotalParallelDv01,
    IReadOnlyList<TradeLine> Trades,
    IReadOnlyList<BucketDelta> Buckets,
    IReadOnlyList<ScenarioLine> Scenarios,
    BriefFlags Flags)
{
    /// <summary>
    /// What the model is shown, and therefore all its text may quote. It leaves out two things on
    /// purpose. Per-trade rows: the number of figures would grow with the book, and the more figures
    /// there are, the more likely an invented one happens to match. Trade ids: other people write
    /// them, so they are kept out of the model's input altogether.
    /// </summary>
    internal string ToNarrationJson() => JsonSerializer.Serialize(
        new
        {
            Currency,
            TradeCount,
            TotalPresentValue,
            TotalParallelDv01,
            Buckets,
            Scenarios,
            Flags = new
            {
                LargestDv01Trade = new { Flags.LargestDv01Trade.PresentValue, Flags.LargestDv01Trade.ParallelDv01 },
                Flags.MostExposedBucket,
                Flags.WorstScenario,
            },
        },
        JsonSerializerOptions.Web);
}

/// <summary>The fixed set of shocks every brief runs, so that two briefs can be compared.</summary>
internal static class BriefShocks
{
    private const double ShockBp = 50;

    public static IReadOnlyList<(string Name, ScenarioShock Shock)> All { get; } =
    [
        ("ParallelUp", new ScenarioShock(ShockBp, 0)),
        ("ParallelDown", new ScenarioShock(-ShockBp, 0)),
        ("Steepener", new ScenarioShock(0, ShockBp)),
        ("Flattener", new ScenarioShock(0, -ShockBp)),
    ];
}

/// <summary>The find_flags step: plain arithmetic and selection over engine results. No model, no I/O.</summary>
internal static class BriefFactsBuilder
{
    private const int AmountDecimals = 2;

    public static RiskBriefState Build(RiskBriefState state)
    {
        if (Problem(state) is { } problem)
        {
            return state with { EngineError = problem };
        }

        var facts = Facts(state);
        return state with { Facts = facts, FactsJson = facts.ToNarrationJson() };
    }

    /// <summary>Why these engine results cannot be added up, or null when they can.</summary>
    private static string? Problem(RiskBriefState state)
    {
        var asked = state.Trades.Select(trade => trade.TradeId).ToList();
        if (!asked.SequenceEqual(state.Valuations.Select(v => v.TradeId), StringComparer.Ordinal)
            || !asked.SequenceEqual(state.Risks.Select(r => r.TradeId), StringComparer.Ordinal))
        {
            // The engine answered for a trade other than the one asked about. Adding those figures
            // up would count one trade twice and another not at all.
            return "The engine returned figures for a different trade than was asked for. Two trade ids may differ only by case.";
        }

        var currencies = Currencies(state);
        if (currencies.Count != 1)
        {
            // Amounts in different currencies cannot be added; a total across them would be a wrong number.
            return $"The book is not in one currency ({string.Join(", ", currencies)}), so it has no totals.";
        }

        return state.Risks.SelectMany(risk => risk.Buckets).Any() ? null : "The engine returned no bucketed risk for this book.";
    }

    private static List<string> Currencies(RiskBriefState state) =>
    [
        .. state.Valuations.Select(v => v.Currency)
            .Concat(state.Risks.Select(r => r.Currency))
            .Concat(state.Scenarios.Select(s => s.Currency))
            .Distinct(StringComparer.Ordinal),
    ];

    private static BriefFacts Facts(RiskBriefState state)
    {
        List<TradeLine> trades =
        [
            .. state.Valuations.Zip(state.Risks, (v, risk) => new TradeLine(v.TradeId, v.PresentValue, v.ParRatePercent, risk.ParallelDv01)),
        ];
        var buckets = Buckets(state.Risks);
        var scenarios = ScenarioLines(state.Scenarios);
        var flags = new BriefFlags(
            trades.MaxBy(trade => Math.Abs(trade.ParallelDv01))!,
            buckets.MaxBy(bucket => Math.Abs(bucket.Delta))!,
            scenarios.MinBy(scenario => scenario.ProfitAndLoss)!);

        return new BriefFacts(
            Currencies(state)[0],
            trades.Count,
            Amount(trades.Sum(trade => trade.PresentValue)),
            Amount(trades.Sum(trade => trade.ParallelDv01)),
            trades,
            buckets,
            scenarios,
            flags);
    }

    private static List<BucketDelta> Buckets(IReadOnlyList<RiskReport> risks) =>
    [
        .. risks.SelectMany(risk => risk.Buckets)
            .GroupBy(bucket => bucket.TenorYears)
            .OrderBy(group => group.Key)
            .Select(group => new BucketDelta(group.Key, Amount(group.Sum(bucket => bucket.Delta)))),
    ];

    private static List<ScenarioLine> ScenarioLines(IReadOnlyList<ScenarioResult> results) =>
    [
        .. BriefShocks.All.Select(shock => new ScenarioLine(
            shock.Name,
            shock.Shock.ParallelBp,
            shock.Shock.SteepenerBp,
            Amount(results.Where(result => result.Shock == shock.Shock).Sum(result => result.ProfitAndLoss)))),
    ];

    private static double Amount(double value) => Math.Round(value, AmountDecimals);
}
