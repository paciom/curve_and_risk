using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Copilot.Briefs;
using CurveRisk.Engine;
using CurveRisk.Workflows;

namespace CurveRisk.Ai.Tests.Briefs;

/// <summary>
/// The engine half of the brief: no model is involved in any of this. The figures are the engine's,
/// the totals are sums taken by code, and a book the engine cannot handle ends the run by name.
/// </summary>
public class RiskBriefFactsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Totals_are_the_sums_of_the_engine_figures_and_the_flags_are_picked_from_them()
    {
        var run = await Brief.Workflow(model: null, StubBook.Engine()).RunAsync(offerSave: false, Ct);
        var facts = run.State.Facts!;

        Assert.Equal(("USD", 2, 300.30, -6.0), (facts.Currency, facts.TradeCount, facts.TotalPresentValue, facts.TotalParallelDv01));
        Assert.Equal([new TradeLine("A-1", 100.10, 3.75, -10), new TradeLine("A-2", 200.20, 3.75, 4)], facts.Trades);
        Assert.Equal([new BucketDelta(2, 4), new BucketDelta(5, -10)], facts.Buckets);
        Assert.Equal(
            [
                new ScenarioLine("ParallelUp", 50, 0, -300),
                new ScenarioLine("ParallelDown", -50, 0, 290),
                new ScenarioLine("Steepener", 0, 50, -20),
                new ScenarioLine("Flattener", 0, -50, 10),
            ],
            facts.Scenarios);
    }

    [Fact]
    public async Task The_flags_are_the_largest_by_size_whatever_the_sign_and_the_worst_scenario_is_the_biggest_loss()
    {
        var run = await Brief.Workflow(model: null, StubBook.Engine()).RunAsync(offerSave: false, Ct);
        var flags = run.State.Facts!.Flags;

        Assert.Equal("A-1", flags.LargestDv01Trade.TradeId);
        Assert.Equal(new BucketDelta(5, -10), flags.MostExposedBucket);
        Assert.Equal(new ScenarioLine("ParallelUp", 50, 0, -300), flags.WorstScenario);
    }

    [Fact]
    public async Task On_the_real_engine_the_totals_match_what_the_engine_gives_trade_by_trade()
    {
        var engine = AnalyticsRiskEngine.CreateDemo();
        var trades = await engine.ListTradesAsync(Ct);
        var presentValue = 0.0;
        var dv01 = 0.0;
        foreach (var trade in trades)
        {
            presentValue += (await engine.PriceTradeAsync(trade.TradeId, Ct)).PresentValue;
            dv01 += (await engine.RunRiskAsync(trade.TradeId, Ct)).ParallelDv01;
        }

        var run = await Brief.Workflow(model: null, engine).RunAsync(offerSave: false, Ct);
        var facts = run.State.Facts!;

        Assert.Equal((trades.Count, Math.Round(presentValue, 2), Math.Round(dv01, 2)), (facts.TradeCount, facts.TotalPresentValue, facts.TotalParallelDv01));
        Assert.Equal(1 + (trades.Count * 6), run.State.ToolCalls.Count);
        Assert.All(run.State.ToolCalls, call => Assert.Equal(ToolOutcome.Succeeded, call.Outcome));
    }

    [Fact]
    public async Task Without_a_model_the_brief_is_still_produced_from_the_engine_alone()
    {
        var run = await Brief.Workflow(model: null, StubBook.Engine()).RunAsync(offerSave: false, Ct);

        Assert.Equal((GraphRunStatus.Ended, RiskBriefGraph.WithoutCommentary), (run.Status, run.Outcome));
        Assert.Equal((CommentaryStatus.NotConfigured, "", 0), (run.State.CommentaryStatus, run.State.Commentary, run.State.ModelCalls));
        Assert.Equal([.. Brief.EnginePath, "narrate", "drop_commentary"], run.Trace.Select(visit => visit.Node));
    }

    [Fact]
    public async Task An_empty_book_ends_the_run_after_one_step()
    {
        var model = new ScriptedModelClient();

        var run = await Brief.Workflow(model, new StubEngine()).RunAsync(offerSave: true, Ct);

        Assert.Equal(RiskBriefGraph.EmptyPortfolio, run.Outcome);
        Assert.Equal(["load_portfolio"], run.Trace.Select(visit => visit.Node));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task A_book_that_cannot_be_listed_ends_the_run_with_the_engines_reason()
    {
        var run = await Brief.Workflow(model: null, new StubEngine { FailListing = true }).RunAsync(offerSave: false, Ct);

        Assert.Equal((RiskBriefGraph.EngineFailed, "list_trades: The book could not be read."), (run.Outcome, run.State.EngineError));
    }

    [Fact]
    public async Task A_tool_that_fails_mid_fan_out_ends_the_run_without_a_model_call_or_internal_detail()
    {
        var model = new ScriptedModelClient();

        var run = await Brief.Workflow(model, new ThrowingOnRiskEngine()).RunAsync(offerSave: false, Ct);

        Assert.Equal((RiskBriefGraph.EngineFailed, "run_risk: 'run_risk' failed unexpectedly."), (run.Outcome, run.State.EngineError));
        Assert.Equal(["load_portfolio", "price_trades", "run_risk", "run_scenarios", "gather"], run.Trace.Select(visit => visit.Node));
        Assert.Empty(model.Requests);
        Assert.Null(run.State.Facts);
    }

    [Fact]
    public async Task A_book_in_two_currencies_has_no_totals_because_the_amounts_cannot_be_added()
    {
        var engine = new StubEngine(StubBook.First, StubBook.Second with { Currency = "EUR" });

        var run = await Brief.Workflow(model: null, engine).RunAsync(offerSave: false, Ct);

        Assert.Equal(RiskBriefGraph.EngineFailed, run.Outcome);
        Assert.Equal("The book is not in one currency (USD, EUR), so it has no totals.", run.State.EngineError);
        Assert.Null(run.State.Facts);
    }

    [Fact]
    public async Task Risk_with_no_buckets_is_an_engine_failure_not_an_empty_ladder()
    {
        var engine = new StubEngine(StubBook.First with { Buckets = [] });

        var run = await Brief.Workflow(model: null, engine).RunAsync(offerSave: false, Ct);

        Assert.Equal((RiskBriefGraph.EngineFailed, "The engine returned no bucketed risk for this book."), (run.Outcome, run.State.EngineError));
    }

    [Fact]
    public async Task Sums_are_rounded_to_cents_so_binary_noise_never_reaches_a_figure()
    {
        var engine = new StubEngine(StubBook.First with { PresentValue = 0.1 }, StubBook.Second with { PresentValue = 0.2 });

        var run = await Brief.Workflow(model: null, engine).RunAsync(offerSave: false, Ct);

        Assert.Equal(0.3, run.State.Facts!.TotalPresentValue);
    }
}
