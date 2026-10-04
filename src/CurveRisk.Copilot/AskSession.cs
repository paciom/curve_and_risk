using System.Diagnostics;

namespace CurveRisk.Copilot;

/// <summary>
/// The mutable state of one question: the transcript, the evidence gathered so far, and the running
/// totals. Kept apart from <see cref="CopilotAgent"/> so the agent itself stays stateless and reusable.
/// </summary>
internal sealed class AskSession
{
    private readonly Activity? _activity;
    private readonly List<Turn> _turns;
    private readonly List<string> _evidence = [];
    private readonly List<string> _quotable = [];
    private readonly List<ToolInvocation> _invocations = [];
    private TokenUsage _usage = TokenUsage.Zero;
    private decimal _cost;
    private int _repairs;

    public AskSession(string question, IReadOnlyList<Turn>? history, Activity? activity)
    {
        _activity = activity;
        _turns = [.. history ?? [], Turn.UserText(question)];
        CollectEvidence();
    }

    public IReadOnlyList<Turn> Turns => _turns;

    public int ModelCalls { get; private set; }

    public void RecordModelCall(ModelResponse response, decimal cost)
    {
        ModelCalls++;
        _usage += response.Usage;
        _cost += cost;
        _turns.Add(new Turn(TurnRole.Assistant, response.Parts));
    }

    /// <summary>
    /// All results go back in ONE user turn; splitting them teaches the model to stop calling tools in parallel.
    /// Successful results become evidence; failed ones contribute only identifiers that may be quoted back.
    /// </summary>
    public void AddToolResults(IReadOnlyList<ExecutedTool> executed)
    {
        _invocations.AddRange(executed.Select(e => e.Invocation));
        _turns.Add(new Turn(TurnRole.User, [.. executed.Select(e => e.Result)]));
        _evidence.AddRange(executed.Where(e => !e.Result.IsError).Select(e => e.Result.Content));
        _quotable.AddRange(executed.Where(e => e.Result.IsError).Select(e => e.Result.Content));
    }

    public GroundingReport CheckGrounding(string answer)
    {
        var report = NumericGrounding.Check(answer, _evidence, _quotable);
        if (!report.IsGrounded)
        {
            CopilotTelemetry.RecordGroundingFailure(_activity, report.Ungrounded.Count);
        }

        return report;
    }

    /// <summary>Sends the model back to fix its figures. False when it has used up its attempts.</summary>
    public bool TryQueueRepair(GroundingReport grounding, int maxRepairs)
    {
        if (_repairs >= maxRepairs)
        {
            return false;
        }

        _repairs++;
        _turns.Add(Turn.HarnessNotice(CopilotMessages.Repair(grounding)));
        return true;
    }

    public CopilotAnswer Finish(AnswerStatus status, string text, GroundingReport? grounding = null, string? draft = null)
    {
        _activity?.SetTag("curverisk.copilot.status", status.ToString());
        return new CopilotAnswer(
            status, text, _invocations, grounding ?? new GroundingReport([]), _usage, _cost, ModelCalls, _turns, draft);
    }

    /// <summary>
    /// Evidence is what a person said and what tools returned. Harness-written turns are skipped, or a
    /// number rejected in an earlier question would support itself in this one.
    /// </summary>
    private void CollectEvidence()
    {
        foreach (var part in _turns.Where(t => t.Role == TurnRole.User && !t.IsSynthetic).SelectMany(t => t.Parts))
        {
            switch (part)
            {
                case TextPart text:
                    _evidence.Add(text.Text);
                    break;
                case ToolResultPart { IsError: false } result:
                    _evidence.Add(result.Content);
                    break;
                case ToolResultPart { IsError: true } failure:
                    _quotable.Add(failure.Content);
                    break;
            }
        }
    }
}
