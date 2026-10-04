using CurveRisk.Ai.Tools;

namespace CurveRisk.Copilot.Briefs;

public enum CommentaryStatus
{
    /// <summary>No commentary has been decided yet, or the run stopped before the commentary step.</summary>
    None,

    /// <summary>The model's text passed the grounding check and is in <see cref="RiskBriefState.Commentary"/>.</summary>
    Grounded,

    /// <summary>The model kept stating figures the engine did not produce, so its text was dropped.</summary>
    Withheld,

    /// <summary>The model could not be reached, refused, or did not finish.</summary>
    Unavailable,
    BudgetExhausted,

    /// <summary>No model provider is configured. The brief is still produced, without commentary.</summary>
    NotConfigured,
}

/// <summary>
/// Everything the risk brief graph knows at one point in a run. Nodes receive it and return a changed
/// copy; nothing else is passed between them, which is what lets a run be paused, stored and resumed.
/// </summary>
public sealed record RiskBriefState
{
    /// <summary>Whether the caller asked for the worst scenario to be offered for saving.</summary>
    public bool OfferSave { get; init; }

    public IReadOnlyList<TradeSummary> Trades { get; init; } = [];

    public IReadOnlyList<TradeValuation> Valuations { get; init; } = [];

    public IReadOnlyList<RiskReport> Risks { get; init; } = [];

    public IReadOnlyList<ScenarioResult> Scenarios { get; init; } = [];

    /// <summary>Every tool call the graph made, in order, whatever its outcome.</summary>
    public IReadOnlyList<ToolInvocation> ToolCalls { get; init; } = [];

    /// <summary>Why the engine results are unusable. Null while everything has succeeded.</summary>
    public string? EngineError { get; init; }

    public BriefFacts? Facts { get; init; }

    /// <summary>
    /// The part of <see cref="Facts"/> the model was shown, as JSON. It is the whole of the grounding
    /// evidence: a figure the model was not given is a figure it may not state.
    /// </summary>
    public string FactsJson { get; init; } = "";

    /// <summary>The conversation with the model in the commentary step, replayed in full on a repair.</summary>
    public IReadOnlyList<Turn> Transcript { get; init; } = [];

    /// <summary>The model's latest text, not yet accepted. Never shown to a user as it stands.</summary>
    public string Draft { get; init; } = "";

    public GroundingReport Grounding { get; init; } = new([]);

    public int Repairs { get; init; }

    public string Commentary { get; init; } = "";

    public CommentaryStatus CommentaryStatus { get; init; }

    public int ModelCalls { get; init; }

    public TokenUsage Usage { get; init; } = TokenUsage.Zero;

    public decimal CostUsd { get; init; }

    /// <summary>The write the graph is waiting for a person to approve. Null when nothing is proposed.</summary>
    public ProposedWrite? Proposal { get; init; }

    public SavedScenario? SavedScenario { get; init; }

    /// <summary>Why an approved save did not happen. Null when it was saved, declined or never attempted.</summary>
    public string? SaveError { get; init; }
}
