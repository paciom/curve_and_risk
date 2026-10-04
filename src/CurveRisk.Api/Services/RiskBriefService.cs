using CurveRisk.Ai.Tools;
using CurveRisk.Contracts.V1;
using CurveRisk.Copilot;
using CurveRisk.Copilot.Briefs;
using CurveRisk.Engine;
using CurveRisk.Workflows;

namespace CurveRisk.Api.Services;

/// <summary>What a brief waiting for approval needs in order to continue: where it stopped, on which market, and how it got there.</summary>
public sealed record RiskBriefCheckpoint(Guid SnapshotId, RiskBriefState State, IReadOnlyList<NodeVisit> Trace);

/// <summary>
/// Produces a risk brief of the stored book on a stored snapshot by running the brief graph, and
/// carries a person's decision back into a brief that paused to ask for one. Works without a model
/// provider: the figures come from the engine, and only the commentary needs the model.
/// </summary>
/// <param name="model">Absent when no provider is configured.</param>
public sealed class RiskBriefService(
    MarketSnapshotService snapshots,
    TradeService trades,
    CopilotConfiguration configuration,
    ICheckpointStore<RiskBriefCheckpoint> checkpoints,
    IModelClient? model = null)
{
    /// <summary>Each trade costs six engine calls, so the size of a brief is bounded.</summary>
    public const int MaxTrades = 50;

    public static GraphResponse Graph { get; } = RiskBriefMapper.ToGraph(RiskBriefWorkflow.Shape);

    public async Task<RiskBriefResponse> CreateAsync(CreateRiskBriefRequest request, CancellationToken cancellationToken)
    {
        var book = await trades.GetBookAsync(MaxTrades + 1, cancellationToken).ConfigureAwait(false);
        if (book.Count > MaxTrades)
        {
            // A brief of part of the book would show totals that are not the book's.
            throw new BookTooLargeException(MaxTrades);
        }

        // Until a person decides, nothing may be written: the gate for the first leg declines everything.
        var workflow = await WorkflowAsync(request.SnapshotId, book, new DenyAllApprovalGate(), cancellationToken).ConfigureAwait(false);
        var run = Usable(await workflow.RunAsync(request.OfferSave, cancellationToken).ConfigureAwait(false));

        var approvalId = run.Status == GraphRunStatus.Paused
            ? checkpoints.Save(new RiskBriefCheckpoint(request.SnapshotId, ForResume(run.State), run.Trace))
            : null;
        return RiskBriefMapper.ToResponse(run, run.Trace, approvalId);
    }

    /// <summary>Continues a paused brief with the decision. An approval id works once, whatever the decision was.</summary>
    public async Task<RiskBriefResponse> DecideAsync(string approvalId, RiskBriefApprovalRequest request, CancellationToken cancellationToken)
    {
        var checkpoint = checkpoints.Take(approvalId) ?? throw new NotFoundException("Risk brief approval", approvalId);
        var proposal = checkpoint.State.Proposal
            ?? throw new InvalidOperationException("A brief paused for approval without a proposal.");

        // Only the save step runs from here, and it needs no trades: the book is not loaded, so a
        // book that has changed since the pause cannot make the decision fail.
        var gate = new ProposalApprovalGate(proposal, request.Approved);
        var workflow = await WorkflowAsync(checkpoint.SnapshotId, [], gate, cancellationToken).ConfigureAwait(false);
        var run = Usable(await workflow.ResumeAsync(checkpoint.State, cancellationToken).ConfigureAwait(false));

        return RiskBriefMapper.ToResponse(run, [.. checkpoint.Trace, .. run.Trace], approvalId: null);
    }

    private async Task<RiskBriefWorkflow> WorkflowAsync(
        Guid snapshotId,
        IReadOnlyList<TradeDefinition> book,
        IApprovalGate gate,
        CancellationToken cancellationToken)
    {
        var market = await snapshots.GetMarketAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        var tools = new ToolExecutor(ToolCatalog.Create(new AnalyticsRiskEngine(market, book)), gate);
        return new RiskBriefWorkflow(model, tools, configuration.Options, configuration.Budget);
    }

    /// <summary>
    /// Keeps what the save step and the final response need, and drops the rest: the conversation
    /// with the model, the rejected draft, and the raw tool results have no use after the pause.
    /// </summary>
    private static RiskBriefState ForResume(RiskBriefState state) =>
        state with { Transcript = [], Draft = "", ToolCalls = [], Trades = [], Valuations = [], Risks = [], Scenarios = [] };

    /// <summary>
    /// Sorts the ways a run can stop into what the caller is told. The engine or the save refusing
    /// the content is a 422, as it is on the other calculation routes. A node that threw, or a run
    /// that did not finish, is a defect and not something a caller can act on.
    /// </summary>
    private static GraphRun<RiskBriefState> Usable(GraphRun<RiskBriefState> run)
    {
        if (run.Status is not (GraphRunStatus.Ended or GraphRunStatus.Paused))
        {
            throw new InvalidOperationException(
                $"The risk brief stopped at '{(run.Trace is [.., var last] ? last.Node : "the start")}' with {run.Status}.", run.Error);
        }

        return run.Outcome switch
        {
            RiskBriefGraph.EngineFailed => throw new RiskEngineException(run.State.EngineError ?? "The engine could not produce this brief."),
            RiskBriefGraph.SaveFailed => throw new RiskEngineException(run.State.SaveError ?? "The scenario could not be saved."),
            _ => run,
        };
    }
}
