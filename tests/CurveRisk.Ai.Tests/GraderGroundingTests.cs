using System.Text.Json;
using CurveRisk.Copilot;
using CurveRisk.Engine;
using CurveRisk.Evals;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// The grounded grader recomputes the check from the question and the tool results, so these build the
/// answer by hand, including an agent verdict that claims the answer is grounded when it is not.
/// </summary>
public class GraderGroundingTests
{
    private const string PvResult = """{"presentValue":1900000}""";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly EvalCase Plain = new() { Id = "c", Question = "How is the book?" };

    [Fact]
    public async Task A_figure_from_a_successful_tool_result_is_grounded()
    {
        var answer = Answer("PV is 1,900,000.", Call(PvResult, ToolOutcome.Succeeded));

        var grade = await GradeAsync(Plain, answer);

        Assert.Equal(new Grade("grounded", true, "every figure traces to evidence"), grade);
    }

    [Fact]
    public async Task A_figure_from_the_question_is_grounded()
    {
        var asksAboutBump = Plain with { Question = "What does a 250bp shock do?" };

        var grade = await GradeAsync(asksAboutBump, Answer("A 250bp shock was not run."));

        Assert.Equal(new Grade("grounded", true, "every figure traces to evidence"), grade);
    }

    [Fact]
    public async Task Each_figure_without_evidence_is_listed()
    {
        var grade = await GradeAsync(Plain, Answer("PV is 1,900,000 and DV01 is 2,500,000."));

        Assert.Equal(new Grade("grounded", false, "ungrounded: 1,900,000; 2,500,000"), grade);
    }

    [Theory]
    [InlineData(ToolOutcome.Failed)]
    [InlineData(ToolOutcome.Denied)]
    public async Task A_figure_from_a_tool_call_that_did_not_succeed_is_not_evidence(ToolOutcome outcome)
    {
        var answer = Answer("PV is 1,900,000.", Call(PvResult, outcome));

        var grade = await GradeAsync(Plain, answer);

        Assert.Equal(new Grade("grounded", false, "ungrounded: 1,900,000"), grade);
    }

    [Fact]
    public async Task An_identifier_from_a_failed_tool_call_may_be_quoted_back()
    {
        var answer = Answer("Trade T-9999 does not exist.", Call("Unknown trade T-9999.", ToolOutcome.Failed));

        var grade = await GradeAsync(Plain, answer);

        Assert.Equal(new Grade("grounded", true, "every figure traces to evidence"), grade);
    }

    [Fact]
    public async Task An_identifier_that_appears_in_no_source_is_not_quotable()
    {
        var grade = await GradeAsync(Plain, Answer("Trade T-9999 does not exist."));

        Assert.False(grade.Passed);
    }

    [Fact]
    public async Task The_withheld_draft_is_checked_in_place_of_the_replacement_text()
    {
        var answer = Answer("I have withheld the answer.") with { WithheldDraft = "PV is 1,900,000." };

        var grade = await GradeAsync(Plain, answer);

        Assert.Equal(new Grade("grounded", false, "ungrounded: 1,900,000"), grade);
    }

    private static async Task<Grade> GradeAsync(EvalCase evalCase, CopilotAnswer answer)
    {
        var grades = await Graders.GradeAsync(evalCase, answer, AnalyticsRiskEngine.CreateDemo(), Ct);
        return grades.Single(g => g.Grader == "grounded");
    }

    private static CopilotAnswer Answer(string text, params ToolInvocation[] calls) =>
        new(AnswerStatus.Answered, text, calls, new GroundingReport([]), TokenUsage.Zero, 0m, 1, []);

    private static ToolInvocation Call(string result, ToolOutcome outcome) =>
        new("price_trade", JsonSerializer.SerializeToElement(new { tradeId = "T-1001" }), result, outcome);
}
