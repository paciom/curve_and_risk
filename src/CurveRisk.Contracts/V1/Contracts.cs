namespace CurveRisk.Contracts.V1;

// Version 1 of the public API. Units are in the names: rates in percent, shocks in basis points,
// dates as yyyy-MM-dd, instants as ISO 8601 UTC. A field is nullable only when absence means something.

/// <summary>One par quote: a tenor such as "5Y" and its rate in percent (3.78 for 3.78%).</summary>
public sealed record QuoteDto(string Tenor, double RatePercent);

/// <summary>The quotes a curve is built from on one date.</summary>
/// <param name="AsOf">yyyy-MM-dd.</param>
/// <param name="Interpolation">LogLinearDiscount, LinearZero or MonotoneCubicLogDiscount.</param>
public sealed record CreateMarketSnapshotRequest(string CurveId, string AsOf, string Interpolation, IReadOnlyList<QuoteDto> Quotes);

public sealed record MarketSnapshotResponse(
    Guid Id,
    string CurveId,
    string AsOf,
    string Interpolation,
    IReadOnlyList<QuoteDto> Quotes,
    DateTimeOffset CreatedUtc);

/// <param name="ZeroRatePercent">Continuously compounded.</param>
public sealed record CurvePillarDto(double TenorYears, double ZeroRatePercent, double DiscountFactor);

public sealed record CurveResponse(Guid SnapshotId, string CurveId, string AsOf, string Interpolation, IReadOnlyList<CurvePillarDto> Pillars);

/// <param name="Direction">PayFixed or ReceiveFixed.</param>
/// <param name="Tenor">Such as "5Y".</param>
public sealed record CreateTradeRequest(
    string TradeId,
    decimal NotionalAmount,
    double FixedRatePercent,
    string Direction,
    string Tenor,
    string? Description);

/// <summary>The editable part of a trade. The id and economic direction are fixed once booked.</summary>
public sealed record UpdateTradeRequest(decimal NotionalAmount, double FixedRatePercent, string Tenor, string? Description);

public sealed record TradeResponse(
    string TradeId,
    decimal NotionalAmount,
    double FixedRatePercent,
    string Direction,
    string Tenor,
    string Description,
    DateTimeOffset CreatedUtc);

public sealed record ValuationRequest(Guid SnapshotId, string TradeId);

/// <param name="PresentValue">From our side, in the trade currency.</param>
public sealed record ValuationResponse(string TradeId, string Currency, double PresentValue, double ParRatePercent, double FixedRatePercent);

/// <param name="ParallelBp">Added to every zero rate. Positive means rates up.</param>
/// <param name="SteepenerBp">Change in the 2s10s zero spread. Positive steepens.</param>
public sealed record ScenarioRunRequest(Guid SnapshotId, string TradeId, double ParallelBp, double SteepenerBp);

public sealed record ScenarioRunResponse(
    string TradeId,
    string Currency,
    double ParallelBp,
    double SteepenerBp,
    double BasePresentValue,
    double ShockedPresentValue,
    double ProfitAndLoss);

public sealed record CreateRiskRunRequest(Guid SnapshotId, IReadOnlyList<string> TradeIds);

/// <param name="Delta">PV change for a +1bp bump of that pillar's zero rate.</param>
public sealed record BucketDeltaDto(double TenorYears, double Delta);

/// <param name="ParallelDv01">PV change for a +1bp parallel shift of zero rates (zero-rate risk, not par-quote risk).</param>
public sealed record TradeRiskDto(string TradeId, string Currency, double ParallelDv01, IReadOnlyList<BucketDeltaDto> Buckets);

/// <summary>A risk calculation that runs in the background. Poll until the status is Completed or Failed.</summary>
/// <param name="Status">Pending, Completed or Failed.</param>
/// <param name="Results">Present once Completed.</param>
/// <param name="Error">Present once Failed.</param>
public sealed record RiskRunResponse(
    Guid Id,
    string Status,
    Guid SnapshotId,
    IReadOnlyList<string> TradeIds,
    IReadOnlyList<TradeRiskDto>? Results,
    string? Error,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? CompletedUtc);

/// <summary>A question for the Risk Copilot about the trades booked, priced on the given snapshot.</summary>
public sealed record CopilotQuestionRequest(Guid SnapshotId, string Question);

/// <param name="Outcome">Succeeded, Failed, Denied or UnknownTool.</param>
public sealed record CopilotToolCallDto(string Name, string Outcome);

/// <param name="Status">
/// Answered, UngroundedWithheld, Refused, Truncated, CallLimitReached, BudgetExhausted or ModelUnavailable.
/// Only Answered carries the model's own text; every other status carries a fixed explanation.
/// </param>
/// <param name="UngroundedFigures">Figures the model stated that no engine result supports. Empty when Answered.</param>
public sealed record CopilotAnswerResponse(
    string Status,
    string Answer,
    IReadOnlyList<CopilotToolCallDto> ToolCalls,
    IReadOnlyList<string> UngroundedFigures,
    int ModelCalls,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd);

/// <summary>Asks for a risk brief of the whole book, priced on the given snapshot.</summary>
/// <param name="OfferSave">
/// When true the brief stops before finishing and proposes saving its worst scenario. Nothing is saved
/// unless the proposal is then approved.
/// </param>
public sealed record CreateRiskBriefRequest(Guid SnapshotId, bool OfferSave = false);

/// <param name="ParallelDv01">PV change for a +1bp parallel shift of zero rates.</param>
public sealed record RiskBriefTradeDto(string TradeId, double PresentValue, double ParRatePercent, double ParallelDv01);

/// <param name="Name">ParallelUp, ParallelDown, Steepener or Flattener.</param>
/// <param name="ProfitAndLoss">Summed over the book.</param>
public sealed record RiskBriefScenarioDto(string Name, double ParallelBp, double SteepenerBp, double ProfitAndLoss);

/// <summary>
/// The figures of a brief. Per-trade figures are the engine's; totals and bucket sums are those figures
/// added up, rounded to two decimals. All amounts are in <see cref="Currency"/>.
/// </summary>
/// <param name="Buckets">PV change of the whole book for +1bp in each pillar's zero rate.</param>
/// <param name="MostExposedBucketTenorYears">The bucket with the largest delta by size.</param>
/// <param name="WorstScenarioName">The scenario with the largest loss.</param>
public sealed record RiskBriefFiguresDto(
    string Currency,
    double TotalPresentValue,
    double TotalParallelDv01,
    IReadOnlyList<RiskBriefTradeDto> Trades,
    IReadOnlyList<BucketDeltaDto> Buckets,
    IReadOnlyList<RiskBriefScenarioDto> Scenarios,
    string LargestDv01TradeId,
    double MostExposedBucketTenorYears,
    string WorstScenarioName);

/// <param name="Status">
/// Grounded, Withheld, Unavailable, BudgetExhausted, NotConfigured or None. Only Grounded carries text:
/// the model's paragraph, every figure of which matched the engine results.
/// </param>
/// <param name="UngroundedFigures">Figures the model stated that the engine results do not support. Empty unless Withheld.</param>
public sealed record RiskBriefCommentaryDto(string Status, string Text, IReadOnlyList<string> UngroundedFigures);

/// <summary>A change the brief is waiting for a person to approve. It is exactly what will be saved.</summary>
/// <param name="ApprovalId">
/// Send the decision to /api/v1/risk-briefs/approvals/{approvalId}. Usable once, and only for a short
/// time: pending approvals are held in memory, expire after 15 minutes and are lost on a restart.
/// </param>
public sealed record RiskBriefProposalDto(string ApprovalId, string ScenarioName, double ParallelBp, double SteepenerBp);

/// <summary>One step of the brief's graph as it ran. Diagnostic: step names follow the implementation and may change.</summary>
/// <param name="Node">The step's name, as in /api/v1/risk-briefs/graph.</param>
/// <param name="Result">Completed or Failed.</param>
public sealed record GraphVisitDto(string Node, string Result, double DurationMs);

/// <param name="Status">
/// Completed, WithoutCommentary, EmptyPortfolio, AwaitingApproval, ScenarioSaved or SaveDeclined.
/// WithoutCommentary means the figures are complete and there is no paragraph; the commentary status says why.
/// </param>
/// <param name="Figures">Absent when the status is EmptyPortfolio.</param>
/// <param name="PendingApproval">Present only while the status is AwaitingApproval.</param>
/// <param name="SavedScenarioId">
/// Present only when the status is ScenarioSaved. The API does not store scenarios yet: the id is the
/// engine's, valid for that request only, and no route reads it back.
/// </param>
/// <param name="Trace">Every step that ran, in order. A step inside the repair cycle appears once per pass.</param>
public sealed record RiskBriefResponse(
    string Status,
    RiskBriefFiguresDto? Figures,
    RiskBriefCommentaryDto Commentary,
    RiskBriefProposalDto? PendingApproval,
    string? SavedScenarioId,
    IReadOnlyList<GraphVisitDto> Trace,
    ModelUsageDto Usage);

/// <summary>What the model calls behind one response used, in total.</summary>
public sealed record ModelUsageDto(int ModelCalls, long InputTokens, long OutputTokens, decimal CostUsd);

/// <summary>A person's decision on a proposed change.</summary>
/// <param name="Approved">True to make the change. Anything else, including leaving it out, declines.</param>
public sealed record RiskBriefApprovalRequest(bool Approved);

/// <param name="Kind">Start, Node, ParallelBranch, Join, Pause or End.</param>
public sealed record GraphNodeDto(string Name, string Kind);

/// <param name="Label">The condition under which the edge is taken. Empty when it is unconditional.</param>
public sealed record GraphEdgeDto(string From, string To, string Label);

/// <summary>
/// The structure of a workflow graph, taken from the definition that runs. Diagnostic: it describes
/// the implementation, so its node names and edges may change without a new API version.
/// </summary>
public sealed record GraphResponse(string Name, IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges);

/// <summary>One page of a collection. Pass <see cref="NextCursor"/> as <c>cursor</c> to get the next; null means the end.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
