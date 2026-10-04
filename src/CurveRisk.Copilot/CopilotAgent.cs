using System.Diagnostics;

namespace CurveRisk.Copilot;

/// <summary>
/// The agent loop, written out rather than delegated to an SDK helper because three things happen
/// inside it that are this product's guarantees: writes wait for a human, every number in the final
/// answer is checked against tool output, and spend is capped.
///
/// The class holds no per-question state (that lives in <see cref="AskSession"/>), so one instance
/// can serve concurrent questions.
/// </summary>
public sealed class CopilotAgent(IModelClient model, ToolExecutor tools, CopilotOptions? options = null, BudgetGuard? budget = null)
{
    private static readonly Lazy<string> SystemPrompt = new(() => EmbeddedPrompt.Load("system.md"));

    private readonly CopilotOptions _options = options ?? new CopilotOptions();
    private readonly BudgetGuard _budget = budget ?? BudgetGuard.Unlimited;

    public async Task<CopilotAnswer> AskAsync(
        string question,
        IReadOnlyList<Turn>? history = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = CopilotTelemetry.StartAgent();
        var session = new AskSession(question, history, activity);

        try
        {
            while (session.ModelCalls < _options.MaxModelCalls)
            {
                if (!_budget.HasBudget)
                {
                    return session.Finish(AnswerStatus.BudgetExhausted, CopilotMessages.BudgetExhausted);
                }

                var response = await CallModelAsync(session, cancellationToken).ConfigureAwait(false);
                var answer = response.StopReason == StopReason.ToolUse
                    ? await RunToolsAsync(session, response, cancellationToken).ConfigureAwait(false)
                    : Conclude(session, response);

                if (answer is not null)
                {
                    return answer;
                }
            }
        }
        catch (ModelClientException ex)
        {
            return session.Finish(
                AnswerStatus.ModelUnavailable,
                ex.IsTransient ? CopilotMessages.TemporarilyUnavailable : CopilotMessages.RequestFailed);
        }

        return session.Finish(AnswerStatus.CallLimitReached, CopilotMessages.CallLimit);
    }

    /// <summary>One model call, costed and counted against the budget before anything else happens.</summary>
    private async Task<ModelResponse> CallModelAsync(AskSession session, CancellationToken cancellationToken)
    {
        using var chat = CopilotTelemetry.StartChat(_options.Model);
        try
        {
            var request = new ModelRequest(SystemPrompt.Value, tools.Specs, [.. session.Turns]);
            var response = await model.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

            var cost = _options.Pricing.CostOf(response.Usage);
            _budget.Record(cost);
            session.RecordModelCall(response, cost);
            CopilotTelemetry.RecordChat(chat, _options.Model, response, cost);
            return response;
        }
        catch (ModelClientException ex)
        {
            chat?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>Returns an answer only when the turn cannot continue; null means "loop again with the tool results".</summary>
    private async Task<CopilotAnswer?> RunToolsAsync(AskSession session, ModelResponse response, CancellationToken cancellationToken)
    {
        var calls = response.Parts.OfType<ToolCallPart>().ToList();
        if (calls.Count == 0)
        {
            return session.Finish(AnswerStatus.Truncated, CopilotMessages.Incomplete);
        }

        session.AddToolResults(await tools.ExecuteAsync(calls, cancellationToken).ConfigureAwait(false));
        return null;
    }

    /// <summary>
    /// Turns a final model response into an answer, or returns null after queueing a grounding repair.
    /// No text leaves here with status Answered unless every figure in it passed the grounding check.
    /// </summary>
    private CopilotAnswer? Conclude(AskSession session, ModelResponse response)
    {
        switch (response.StopReason)
        {
            case StopReason.Refusal:
                return session.Finish(AnswerStatus.Refused, CopilotMessages.Refused);
            case StopReason.MaxTokens:
                return session.Finish(AnswerStatus.Truncated, CopilotMessages.CutOff);
            case StopReason.Other:
                return session.Finish(AnswerStatus.Truncated, CopilotMessages.Incomplete);
        }

        var text = string.Join("\n", response.Parts.OfType<TextPart>().Select(p => p.Text)).Trim();
        if (text.Length == 0)
        {
            return session.Finish(AnswerStatus.Truncated, CopilotMessages.Incomplete);
        }

        var grounding = session.CheckGrounding(text);
        if (grounding.IsGrounded)
        {
            return session.Finish(AnswerStatus.Answered, text, grounding);
        }

        return session.TryQueueRepair(grounding, _options.MaxGroundingRepairs)
            ? null
            : session.Finish(AnswerStatus.UngroundedWithheld, CopilotMessages.Withheld, grounding, draft: text);
    }
}
