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

/// <summary>One page of a collection. Pass <see cref="NextCursor"/> as <c>cursor</c> to get the next; null means the end.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
