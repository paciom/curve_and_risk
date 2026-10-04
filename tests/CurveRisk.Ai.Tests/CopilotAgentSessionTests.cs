using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Engine;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests;

/// <summary>What one question carries with it: the history, the evidence, the repair notice and the spend.</summary>
public class CopilotAgentSessionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly Turn[] PricedEarlier =
    [
        Turn.UserText("Price the swap."),
        new Turn(TurnRole.Assistant, [new ToolCallPart("call_0", "price_trade", JsonSerializer.SerializeToElement(new { tradeId = "T-1001" }))]),
        new Turn(TurnRole.User, [new ToolResultPart("call_0", """{"presentValue":1672655.53}""", IsError: false)]),
        new Turn(TurnRole.Assistant, [new TextPart("Priced.")]),
    ];

    private static readonly Turn[] FailedEarlier =
    [
        Turn.UserText("Price the swap."),
        new Turn(TurnRole.Assistant, [new ToolCallPart("call_0", "price_trade", JsonSerializer.SerializeToElement(new { tradeId = "T-9999" }))]),
        new Turn(TurnRole.User, [new ToolResultPart("call_0", "Unknown trade. Known trades: T-1001 and 4242 more.", IsError: true)]),
        new Turn(TurnRole.Assistant, [new TextPart("That trade does not exist.")]),
    ];

    private static CopilotAgent Agent(IModelClient model, BudgetGuard? budget = null) => new(
        model,
        new ToolExecutor(ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()), new DenyAllApprovalGate()),
        budget: budget);

    [Fact]
    public async Task Earlier_turns_are_sent_to_the_model_ahead_of_the_new_question()
    {
        var model = new ScriptedModelClient(Says("Done."));

        await Agent(model).AskAsync("And now?", history: PricedEarlier, cancellationToken: Ct);

        var sent = model.Requests[0].Turns;
        Assert.Equal(PricedEarlier, sent.Take(4));
        Assert.Equal(Turn.UserText("And now?").Parts, sent[4].Parts);
        Assert.Equal(5, sent.Count);
    }

    [Fact]
    public async Task Figures_in_the_question_may_be_repeated_in_the_answer()
    {
        var model = new ScriptedModelClient(Says("A 250,000 notional was requested."));

        var answer = await Agent(model).AskAsync("What if the notional is 250,000?", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(1, answer.ModelCalls);
    }

    [Fact]
    public async Task A_successful_tool_result_from_an_earlier_question_is_still_evidence()
    {
        var model = new ScriptedModelClient(Says("The PV was 1,672,655.53."));

        var answer = await Agent(model).AskAsync("What was the PV again?", history: PricedEarlier, cancellationToken: Ct);

        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(1, answer.ModelCalls);
    }

    [Fact]
    public async Task An_identifier_in_an_earlier_failed_tool_result_may_be_quoted()
    {
        var model = new ScriptedModelClient(Says("The only known trade is T-1001."));

        var answer = await Agent(model).AskAsync("Which trades exist?", history: FailedEarlier, cancellationToken: Ct);

        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(1, answer.ModelCalls);
    }

    [Fact]
    public async Task An_identifier_in_a_tool_error_from_this_question_may_be_quoted()
    {
        var failing = new StubFunction("find_trade", () => throw new RiskEngineException("Unknown trade. Known trades: T-7001."));
        var model = new ScriptedModelClient(Calls(("find_trade", new { })), Says("The only known trade is T-7001."));
        var agent = new CopilotAgent(model, new ToolExecutor([new CopilotTool(failing, RequiresApproval: false)], new DenyAllApprovalGate()));

        var answer = await agent.AskAsync("Which trades exist?", cancellationToken: Ct);

        Assert.Equal(AnswerStatus.Answered, answer.Status);
        Assert.Equal(2, answer.ModelCalls);
    }

    [Fact]
    public async Task A_figure_in_an_earlier_failed_tool_result_is_not_evidence()
    {
        var model = new ScriptedModelClient(Says("There are 4,242 more trades."), Says("There are 4,242 more trades."));

        var answer = await Agent(model).AskAsync("How many trades exist?", history: FailedEarlier, cancellationToken: Ct);

        Assert.Equal(AnswerStatus.UngroundedWithheld, answer.Status);
        Assert.Equal(["4,242"], answer.Grounding.Ungrounded);
    }

    [Fact]
    public async Task The_repair_notice_is_a_harness_turn_that_lists_each_rejected_figure()
    {
        var model = new ScriptedModelClient(Says("DV01 is 123,456 and PV is 987,654."), Says("The engine did not return those."));

        await Agent(model).AskAsync("DV01?", cancellationToken: Ct);

        var notice = model.Requests[1].Turns[^1];
        Assert.Equal(TurnRole.User, notice.Role);
        Assert.True(notice.IsSynthetic);
        Assert.Equal(
            "<grounding_check>\n" +
            "Automated check, not a message from the user. These figures in your last answer do not match any tool result " +
            "or anything the user said: 123,456; 987,654.\n" +
            "Rewrite the answer. For each one, either call the tool that produces it and quote the result, or remove it " +
            "and say the engine did not return that figure. Do not compute it yourself.\n" +
            "</grounding_check>",
            Assert.IsType<TextPart>(Assert.Single(notice.Parts)).Text);
    }

    [Fact]
    public async Task Text_blocks_of_one_answer_are_joined_by_line_breaks()
    {
        var model = new ScriptedModelClient(
            _ => new ModelResponse([new TextPart("First."), new TextPart("Second.")], StopReason.EndTurn, TokenUsage.Zero, "scripted"));

        var answer = await Agent(model).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Equal("First.\nSecond.", answer.Text);
    }

    [Theory]
    [InlineData(true, "The Copilot is temporarily unavailable. Please try again shortly.")]
    [InlineData(false, "The Copilot could not process this request.")]
    public async Task A_provider_failure_tells_the_user_whether_trying_again_may_help(bool transient, string expected)
    {
        var model = new ScriptedModelClient(_ => throw new ModelClientException("boom", transient, new IOException()));

        var answer = await Agent(model).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Equal(expected, answer.Text);
    }

    [Fact]
    public async Task Every_model_call_is_charged_to_the_daily_budget()
    {
        var budget = new BudgetGuard(dailyLimitUsd: 10m, new FakeClock(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)));
        var model = new ScriptedModelClient(Calls(("list_trades", new { })), Says("Done."));

        var answer = await Agent(model, budget).AskAsync("Go.", cancellationToken: Ct);

        // Two calls of 1,000 input tokens at $4 and 100 output tokens at $20 per million.
        Assert.Equal(0.012m, budget.SpentTodayUsd);
        Assert.Equal(0.012m, answer.CostUsd);
    }

    [Fact]
    public async Task The_system_prompt_keeps_its_line_breaks()
    {
        var model = new ScriptedModelClient(Says("Done."));

        await Agent(model).AskAsync("Hi.", cancellationToken: Ct);

        Assert.Contains("\n\n## Where numbers come from\n\n", model.Requests[0].SystemPrompt, StringComparison.Ordinal);
    }
}
