using System.Diagnostics;
using CurveRisk.Workflows;

namespace CurveRisk.Copilot.Briefs;

/// <summary>Text the brief graph writes to the model, in one place so the wording is reviewed together.</summary>
internal static class BriefMessages
{
    public static string Facts(string factsJson) =>
        "<engine_results>\n" + factsJson + "\n</engine_results>\n" +
        "Write the commentary for this risk brief.";

    public static string Repair(GroundingReport grounding) =>
        "<grounding_check>\n" +
        "Automated check, not a message from the user. These figures in your commentary do not match any figure in " +
        $"the engine results: {string.Join("; ", grounding.Ungrounded)}.\n" +
        "Rewrite the commentary. For each one, either quote the figure exactly as it appears in the engine results " +
        "or remove it. Do not compute a figure yourself.\n" +
        "</grounding_check>";
}

/// <summary>
/// The narrate step, the only one that calls a model. It has no tools: the figures are already in
/// the state, so the model's job is prose. A failure of any kind leaves the draft empty and says
/// why; the brief is then delivered without commentary instead of failing.
/// </summary>
/// <param name="model">Null when no provider is configured.</param>
internal sealed class NarrateNode(IModelClient? model, CopilotOptions options, BudgetGuard budget) : IGraphNode<RiskBriefState>
{
    private static readonly Lazy<string> Prompt = new(() => EmbeddedPrompt.Load("brief.md"));

    public async Task<RiskBriefState> RunAsync(RiskBriefState state, CancellationToken cancellationToken)
    {
        if (model is null)
        {
            return WithoutDraft(state, CommentaryStatus.NotConfigured);
        }

        if (!budget.HasBudget)
        {
            return WithoutDraft(state, CommentaryStatus.BudgetExhausted);
        }

        IReadOnlyList<Turn> turns = state.Transcript.Count > 0 ? state.Transcript : [Turn.UserText(BriefMessages.Facts(state.FactsJson))];
        try
        {
            var response = await CallAsync(model, turns, cancellationToken).ConfigureAwait(false);
            return Record(state, turns, response);
        }
        catch (ModelClientException)
        {
            return WithoutDraft(state, CommentaryStatus.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout inside the provider call, not the caller going away.
            return WithoutDraft(state, CommentaryStatus.Unavailable);
        }
    }

    private async Task<ModelResponse> CallAsync(IModelClient client, IReadOnlyList<Turn> turns, CancellationToken cancellationToken)
    {
        using var chat = CopilotTelemetry.StartChat(options.Model);
        try
        {
            var response = await client.CompleteAsync(new ModelRequest(Prompt.Value, [], turns), cancellationToken).ConfigureAwait(false);
            var cost = options.Pricing.CostOf(response.Usage);
            budget.Record(cost);
            CopilotTelemetry.RecordChat(chat, options.Model, response, cost);
            return response;
        }
        catch (ModelClientException ex)
        {
            chat?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private RiskBriefState Record(RiskBriefState state, IReadOnlyList<Turn> turns, ModelResponse response)
    {
        // Only a turn the model ended itself is a draft. A refusal, a cut-off or a tool call it was never offered is not.
        var draft = response.StopReason == StopReason.EndTurn
            ? string.Join("\n", response.Parts.OfType<TextPart>().Select(part => part.Text)).Trim()
            : "";

        return state with
        {
            Transcript = [.. turns, new Turn(TurnRole.Assistant, response.Parts)],
            Draft = draft,
            Grounding = new GroundingReport([]),
            CommentaryStatus = draft.Length == 0 ? CommentaryStatus.Unavailable : CommentaryStatus.None,
            ModelCalls = state.ModelCalls + 1,
            Usage = state.Usage + response.Usage,
            CostUsd = state.CostUsd + options.Pricing.CostOf(response.Usage),
        };
    }

    /// <summary>Also forgets what an earlier draft got wrong: with no draft, there are no figures to report as ungrounded.</summary>
    private static RiskBriefState WithoutDraft(RiskBriefState state, CommentaryStatus why) =>
        state with { Draft = "", Grounding = new GroundingReport([]), CommentaryStatus = why };
}

/// <summary>
/// The steps that decide what happens to a draft. They are rules over the state: the check is the
/// same <see cref="NumericGrounding"/> the Copilot loop uses.
/// </summary>
internal static class CommentaryRules
{
    /// <summary>
    /// The evidence is exactly what the model was shown and nothing else. The raw tool results are
    /// left out: they hold figures the model never saw (notionals and fixed rates that whoever books
    /// a trade chooses, and several per trade), and every extra figure is one more value an invented
    /// number can match by chance.
    /// </summary>
    public static RiskBriefState Check(RiskBriefState state)
    {
        var report = NumericGrounding.Check(state.Draft, [state.FactsJson]);
        if (!report.IsGrounded)
        {
            CopilotTelemetry.RecordGroundingFailure(Activity.Current, report.Ungrounded.Count);
        }

        return state with { Grounding = report };
    }

    public static RiskBriefState RequestRepair(RiskBriefState state) => state with
    {
        Repairs = state.Repairs + 1,
        Transcript = [.. state.Transcript, Turn.HarnessNotice(BriefMessages.Repair(state.Grounding))],
    };

    public static RiskBriefState Accept(RiskBriefState state) =>
        state with { Commentary = state.Draft, CommentaryStatus = CommentaryStatus.Grounded };

    /// <summary>Keeps the reason already recorded (unavailable, no budget); a draft that reached here unaccepted was withheld.</summary>
    public static RiskBriefState Drop(RiskBriefState state) => state with
    {
        Commentary = "",
        CommentaryStatus = state.CommentaryStatus == CommentaryStatus.None ? CommentaryStatus.Withheld : state.CommentaryStatus,
    };

    public static RiskBriefState ProposeSave(RiskBriefState state) =>
        state with { Proposal = ProposedWrite.ForWorst(state.Facts!.Flags.WorstScenario) };
}
