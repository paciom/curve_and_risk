using CurveRisk.Copilot;
using CurveRisk.Copilot.Briefs;
using CurveRisk.Workflows;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests.Briefs;

/// <summary>
/// What an independent review broke, kept as tests. Each one is a way the brief could show a figure
/// the engine did not produce, fail on something a caller did, or report an outcome that was not true.
/// </summary>
public class RiskBriefGuardTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_model_is_shown_totals_and_flags_but_no_trade_id_and_no_per_trade_row()
    {
        var model = new ScriptedModelClient(Says("No figures."));

        await Brief.Workflow(model, StubBook.Engine()).RunAsync(offerSave: false, Ct);

        var shown = Brief.FactsJsonIn(model.Requests[0]);
        Assert.DoesNotContain("A-1", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("tradeId", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("200.2", shown, StringComparison.Ordinal);
        Assert.StartsWith("{\"currency\":\"USD\",\"tradeCount\":2,\"totalPresentValue\":300.3,\"totalParallelDv01\":-6,\"buckets\":[", shown, StringComparison.Ordinal);
        Assert.Contains("\"largestDv01Trade\":{\"presentValue\":100.1,\"parallelDv01\":-10}", shown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The book loses USD 1,000,000.")] // the notional of every stub trade: in a tool result, never shown to the model
    [InlineData("Trade A-2 is worth USD 200.20.")] // a per-trade figure the model was not given
    [InlineData("The fixed rate is 3.5%.")]
    public async Task A_figure_the_engine_returned_but_the_model_was_not_shown_is_not_evidence(string draft)
    {
        var model = new ScriptedModelClient(Says(draft), Says(draft));

        var run = await Brief.Workflow(model, StubBook.Engine()).RunAsync(offerSave: false, Ct);

        Assert.Equal(CommentaryStatus.Withheld, run.State.CommentaryStatus);
    }

    [Fact]
    public async Task A_trade_id_shaped_like_an_amount_cannot_be_quoted_as_one()
    {
        var engine = new StubEngine(StubBook.First with { TradeId = "USD-9000000" });
        const string draft = "Under the parallel up shock the book shows a loss of USD-9000000.";
        var model = new ScriptedModelClient(Says(draft), Says(draft));

        var run = await Brief.Workflow(model, engine).RunAsync(offerSave: false, Ct);

        Assert.Equal(CommentaryStatus.Withheld, run.State.CommentaryStatus);
        Assert.DoesNotContain("USD-9000000", Brief.FactsJsonIn(model.Requests[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Figures_returned_for_a_different_trade_than_was_asked_for_end_the_run_instead_of_being_added_up()
    {
        var engine = new StubEngine(StubBook.First, StubBook.Second) { AnswerAs = "A-1" };

        var run = await Brief.Workflow(model: null, engine).RunAsync(offerSave: false, Ct);

        Assert.Equal((GraphRunStatus.Ended, RiskBriefGraph.EngineFailed), (run.Status, run.Outcome));
        Assert.StartsWith("The engine returned figures for a different trade than was asked for.", run.State.EngineError, StringComparison.Ordinal);
        Assert.Null(run.State.Facts);
    }

    [Fact]
    public async Task When_the_repair_call_fails_the_rejected_figures_of_the_first_draft_are_not_reported()
    {
        var model = new ScriptedModelClient(
            Says("Total PV is USD 987,654."),
            _ => throw new ModelClientException("down", isTransient: true, new TimeoutException()));

        var run = await Brief.Workflow(model, StubBook.Engine()).RunAsync(offerSave: false, Ct);

        Assert.Equal(CommentaryStatus.Unavailable, run.State.CommentaryStatus);
        Assert.Empty(run.State.Grounding.Ungrounded);
    }

    [Fact]
    public async Task A_timeout_inside_the_model_call_gives_a_brief_without_commentary_not_a_failed_brief()
    {
        var model = new ScriptedModelClient(_ => throw new TaskCanceledException("The request timed out."));

        var run = await Brief.Workflow(model, StubBook.Engine()).RunAsync(offerSave: false, Ct);

        Assert.Equal((GraphRunStatus.Ended, CommentaryStatus.Unavailable), (run.Status, run.State.CommentaryStatus));
        Assert.NotNull(run.State.Facts);
    }

    [Fact]
    public async Task Repairs_are_model_calls_and_stay_under_the_per_request_call_limit()
    {
        var invented = Enumerable.Repeat(Says("Total PV is USD 987,654."), 3).ToArray();
        var options = new CopilotOptions { MaxGroundingRepairs = 40, MaxModelCalls = 3 };

        var run = await new RiskBriefWorkflow(new ScriptedModelClient(invented), Brief.Tools(StubBook.Engine()), options).RunAsync(offerSave: false, Ct);

        Assert.Equal((GraphRunStatus.Ended, RiskBriefGraph.WithoutCommentary, 3), (run.Status, run.Outcome, run.State.ModelCalls));
    }

    [Fact]
    public async Task Every_repair_the_options_allow_fits_inside_the_step_limit()
    {
        var invented = Enumerable.Repeat(Says("Total PV is USD 987,654."), 12).ToArray();
        var options = new CopilotOptions { MaxGroundingRepairs = 11 };

        var run = await new RiskBriefWorkflow(new ScriptedModelClient(invented), Brief.Tools(StubBook.Engine()), options).RunAsync(offerSave: true, Ct);

        Assert.Equal((GraphRunStatus.Paused, 12), (run.Status, run.State.ModelCalls));
    }

    [Fact]
    public async Task A_book_that_loses_nothing_under_any_shock_has_no_worst_case_to_offer()
    {
        var flat = StubBook.First with { ScenarioPnl = [0, 5, 0, 12] };

        var run = await Brief.Workflow(model: null, new StubEngine(flat)).RunAsync(offerSave: true, Ct);

        Assert.Equal((GraphRunStatus.Ended, RiskBriefGraph.WithoutCommentary), (run.Status, run.Outcome));
        Assert.Null(run.State.Proposal);
    }

    [Fact]
    public async Task An_approved_save_that_the_engine_rejects_is_a_failure_not_a_decline()
    {
        var paused = (await Brief.Workflow(model: null, StubBook.Engine()).RunAsync(offerSave: true, Ct)).State;
        var failing = new StubEngine(StubBook.First, StubBook.Second) { FailSaving = true };

        var run = await Brief.Workflow(model: null, failing, new ProposalApprovalGate(paused.Proposal!, approved: true)).ResumeAsync(paused, Ct);

        Assert.Equal(RiskBriefGraph.SaveFailed, run.Outcome);
        Assert.Equal("save_scenario: A scenario with that name already exists.", run.State.SaveError);
        Assert.Equal(ToolOutcome.Failed, run.State.ToolCalls[^1].Outcome);
    }

    [Fact]
    public async Task A_declined_save_carries_no_error()
    {
        var paused = (await Brief.Workflow(model: null, StubBook.Engine()).RunAsync(offerSave: true, Ct)).State;

        var run = await Brief.Workflow(model: null, StubBook.Engine()).ResumeAsync(paused, Ct);

        Assert.Equal(RiskBriefGraph.SaveDeclined, run.Outcome);
        Assert.Null(run.State.SaveError);
    }

    [Fact]
    public async Task A_cancelled_brief_stops_between_groups_of_engine_calls_instead_of_running_them_all()
    {
        using var cancelled = new CancellationTokenSource();
        var trades = Enumerable.Range(0, 40).Select(index => StubBook.First with { TradeId = $"C-{index}" }).ToArray();
        var engine = new StubEngine(trades) { OnPrice = count => CancelOnFirst(count, cancelled) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Brief.Workflow(model: null, engine).RunAsync(offerSave: false, cancelled.Token));

        Assert.InRange(engine.Priced, 1, 8);
    }

    private static void CancelOnFirst(int count, CancellationTokenSource source)
    {
        if (count == 0)
        {
            source.Cancel();
        }
    }
}
