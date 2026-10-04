using CurveRisk.Contracts.V1;
using CurveRisk.Copilot.Briefs;
using CurveRisk.Workflows;

namespace CurveRisk.Api.Services;

/// <summary>Shapes a brief run as the public contract. The rejected draft and the model transcript stay behind.</summary>
internal static class RiskBriefMapper
{
    private const string AwaitingApproval = "AwaitingApproval";

    /// <param name="trace">The whole run, including the leg before a pause.</param>
    /// <param name="approvalId">Set while the run is waiting for a decision.</param>
    public static RiskBriefResponse ToResponse(GraphRun<RiskBriefState> run, IReadOnlyList<NodeVisit> trace, string? approvalId)
    {
        var state = run.State;
        return new RiskBriefResponse(
            run.Status == GraphRunStatus.Paused ? AwaitingApproval : run.Outcome,
            state.Facts is { } facts ? ToFigures(facts) : null,
            new RiskBriefCommentaryDto(
                state.CommentaryStatus.ToString(),
                state.Commentary,
                state.CommentaryStatus == CommentaryStatus.Withheld ? state.Grounding.Ungrounded : []),
            approvalId is not null && state.Proposal is { } proposal
                ? new RiskBriefProposalDto(approvalId, proposal.ScenarioName, proposal.ParallelBp, proposal.SteepenerBp)
                : null,
            state.SavedScenario?.ScenarioId,
            [.. trace.Select(visit => new GraphVisitDto(visit.Node, visit.Result.ToString(), visit.DurationMs))],
            new ModelUsageDto(state.ModelCalls, state.Usage.InputTokens, state.Usage.OutputTokens, state.CostUsd));
    }

    public static GraphResponse ToGraph(GraphShape shape) => new(
        shape.Name,
        [.. shape.Nodes.Select(node => new GraphNodeDto(node.Name, node.Kind.ToString()))],
        [.. shape.Edges.Select(edge => new GraphEdgeDto(edge.From, edge.To, edge.Label))]);

    private static RiskBriefFiguresDto ToFigures(BriefFacts facts) => new(
        facts.Currency,
        facts.TotalPresentValue,
        facts.TotalParallelDv01,
        [.. facts.Trades.Select(trade => new RiskBriefTradeDto(trade.TradeId, trade.PresentValue, trade.ParRatePercent, trade.ParallelDv01))],
        [.. facts.Buckets.Select(bucket => new BucketDeltaDto(bucket.TenorYears, bucket.Delta))],
        [.. facts.Scenarios.Select(line => new RiskBriefScenarioDto(line.Name, line.ParallelBp, line.SteepenerBp, line.ProfitAndLoss))],
        facts.Flags.LargestDv01Trade.TradeId,
        facts.Flags.MostExposedBucket.TenorYears,
        facts.Flags.WorstScenario.Name);
}
