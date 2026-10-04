using CurveRisk.Ai.Tools;
using CurveRisk.Ai.Tools.Fixtures;
using CurveRisk.Copilot;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests;

public class CopilotAgentTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly FixtureRiskEngine _engine = new();

    private CopilotAgent Agent(IModelClient model, IApprovalGate? gate = null, CopilotOptions? options = null, BudgetGuard? budget = null) =>
        new(model, new ToolExecutor(ToolCatalog.Create(_engine), gate ?? new DenyAllApprovalGate()), options, budget);

    [Fact]
    public async Task Answers_from_a_tool_result()
    {
        var model = new ScriptedModelClient(
            Calls(("price_trade", new { tradeId = "T-1001" })),
            SaysFromLastResult(r => $"PV is USD {r.GetProperty("presentValue").GetDouble():N2}."));

        var answer = await Agent(model).AskAsync("What is the PV of T-1001?", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(2, answer.ModelCalls);
        Assert.Equal(ToolOutcome.Succeeded, Assert.Single(answer.ToolCalls).Outcome);
        Assert.Equal(2000, answer.Usage.InputTokens);
        Assert.Equal(ModelPricing.ClaudeOpus55.CostOf(answer.Usage), answer.CostUsd);
    }

    [Fact]
    public async Task Parallel_tool_calls_return_in_one_user_turn_in_call_order()
    {
        var model = new ScriptedModelClient(
            Calls(("price_trade", new { tradeId = "T-1001" }), ("price_trade", new { tradeId = "T-1002" })),
            Says("Done."));

        await Agent(model).AskAsync("Price both.", cancellationToken: Ct);

        var followUp = model.Requests[1].Turns;
        Assert.Equal([TurnRole.User, TurnRole.Assistant, TurnRole.User], followUp.Select(t => t.Role));
        var results = followUp[2].Parts.Cast<ToolResultPart>().ToList();
        Assert.Equal(["call_0", "call_1"], results.Select(r => r.ToolCallId));
        Assert.Contains("T-1002", results[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thinking_blocks_are_replayed_unchanged()
    {
        var thinking = new ThinkingPart(string.Empty, "sig-abc");
        var model = new ScriptedModelClient(
            _ => new ModelResponse(
                [thinking, new ToolCallPart("call_0", "list_trades", default)],
                StopReason.ToolUse,
                TokenUsage.Zero,
                "scripted"),
            Says("Done."));

        await Agent(model).AskAsync("List trades.", cancellationToken: Ct);

        Assert.Same(thinking, model.Requests[1].Turns[1].Parts[0]);
    }

    [Fact]
    public async Task A_fabricated_number_triggers_one_repair_and_the_corrected_answer_is_returned()
    {
        var model = new ScriptedModelClient(
            Calls(("price_trade", new { tradeId = "T-1001" })),
            Says("PV is about USD 1,900,000."),
            SaysFromLastResult(r => $"PV is USD {r.GetProperty("presentValue").GetDouble():N2}."));

        var answer = await Agent(model).AskAsync("PV of T-1001?", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(3, answer.ModelCalls);
        var repair = Assert.IsType<TextPart>(model.Requests[2].Turns[^1].Parts.Single());
        Assert.Contains("1,900,000", repair.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_number_that_stays_fabricated_is_withheld_from_the_user()
    {
        var model = new ScriptedModelClient(
            Calls(("price_trade", new { tradeId = "T-1001" })),
            Says("PV is about USD 1,900,000."),
            Says("PV is definitely USD 1,900,000."));

        var answer = await Agent(model).AskAsync("PV of T-1001?", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.UngroundedWithheld, answer.Status);
        Assert.DoesNotContain("1,900,000", answer.Text, StringComparison.Ordinal);
        Assert.Contains("1,900,000", answer.WithheldDraft, StringComparison.Ordinal);
        Assert.Equal(["USD 1,900,000"], answer.Grounding.Ungrounded);
    }

    [Fact]
    public async Task A_rejected_number_does_not_become_evidence_in_the_next_question()
    {
        var first = new ScriptedModelClient(Says("DV01 is USD 123,456."), Says("The engine did not return that."));
        var answer1 = await Agent(first).AskAsync("DV01?", cancellationToken: Ct);
        Assert.Equal(AnswerStatus.Answered, answer1.Status);

        // The transcript now contains the repair notice, which names 123,456.
        var second = new ScriptedModelClient(Says("DV01 is USD 123,456."), Says("DV01 is USD 123,456."));
        var answer2 = await Agent(second).AskAsync("And again?", history: answer1.Transcript, cancellationToken: Ct);

        Assert.Equal(AnswerStatus.UngroundedWithheld, answer2.Status);
    }

    [Fact]
    public async Task Numbers_from_failed_tool_calls_are_not_evidence()
    {
        var model = new ScriptedModelClient(
            Calls(("price_trade", new { tradeId = "T-9999" })),
            Says("T-9999 is worth USD 9,999."),
            Says("That trade does not exist."));

        var answer = await Agent(model).AskAsync("Price it.", cancellationToken: Ct);

        Assert.Equal(3, answer.ModelCalls);
        Assert.Equal(ToolOutcome.Failed, answer.ToolCalls[0].Outcome);
    }

    [Theory]
    [InlineData(StopReason.Refusal, AnswerStatus.Refused)]
    [InlineData(StopReason.MaxTokens, AnswerStatus.Truncated)]
    [InlineData(StopReason.Other, AnswerStatus.Truncated)]
    [InlineData(StopReason.ToolUse, AnswerStatus.Truncated)]
    [InlineData(StopReason.EndTurn, AnswerStatus.Truncated)]
    public async Task Terminal_stop_reasons_and_empty_responses_map_to_statuses(StopReason reason, AnswerStatus expected)
    {
        var answer = await Agent(new ScriptedModelClient(Stops(reason))).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Equal(expected, answer.Status);
    }

    [Fact]
    public async Task The_loop_stops_at_the_call_limit()
    {
        var loop = Calls(("list_trades", new { }));
        var model = new ScriptedModelClient(loop, loop, loop, loop);

        var answer = await Agent(model, options: new CopilotOptions { MaxModelCalls = 3 }).AskAsync("Go.", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.CallLimitReached, answer.Status);
        Assert.Equal(3, model.Requests.Count);
    }

    [Fact]
    public async Task No_model_call_is_made_once_the_daily_budget_is_spent()
    {
        var budget = new BudgetGuard(dailyLimitUsd: 1m);
        budget.Record(1m);
        var model = new ScriptedModelClient();

        var answer = await Agent(model, budget: budget).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.BudgetExhausted, answer.Status);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void The_budget_resets_at_the_utc_day_boundary()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 3, 23, 59, 0, TimeSpan.Zero));
        var budget = new BudgetGuard(1m, clock);
        budget.Record(2m);
        Assert.False(budget.HasBudget);

        clock.Now = clock.Now.AddMinutes(2);

        Assert.True(budget.HasBudget);
        Assert.Equal(0m, budget.SpentTodayUsd);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provider_failures_become_a_status_not_an_exception(bool transient)
    {
        var model = new ScriptedModelClient(_ => throw new ModelClientException("boom", transient, new IOException()));

        var answer = await Agent(model).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.ModelUnavailable, answer.Status);
        Assert.DoesNotContain("boom", answer.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_system_prompt_and_tool_list_are_byte_identical_across_calls()
    {
        // Prompt caching is a prefix match; anything volatile here would silently disable it.
        var model = new ScriptedModelClient(Calls(("list_trades", new { })), Says("Done."));

        await Agent(model).AskAsync("Go.", cancellationToken: Ct);

        Assert.Equal(model.Requests[0].SystemPrompt, model.Requests[1].SystemPrompt);
        Assert.Equal(
            model.Requests[0].Tools.Select(t => t.InputSchema.GetRawText()),
            model.Requests[1].Tools.Select(t => t.InputSchema.GetRawText()));
        Assert.DoesNotContain("\r", model.Requests[0].SystemPrompt, StringComparison.Ordinal);
    }
}
