using System.Diagnostics;

namespace CurveRisk.Workflows;

/// <summary>Span names for graph runs. A host opts in with <c>AddSource(GraphTelemetry.Name)</c>.</summary>
public static class GraphTelemetry
{
    public const string Name = "CurveRisk.Workflows";

    private static readonly ActivitySource Source = new(Name);

    internal static Activity? StartRun(string graph) =>
        Source.StartActivity($"run_graph {graph}")?.SetTag("curverisk.graph.name", graph);

    internal static Activity? StartNode(string graph, string node) =>
        Source.StartActivity($"run_node {node}")?.SetTag("curverisk.graph.name", graph).SetTag("curverisk.graph.node", node);
}

/// <summary>
/// Executes a graph: run the current node, let its edge choose the next from the new state, repeat
/// until an end or a pause. The runner knows nothing about models or tools; what it guarantees is
/// that the path taken is one the definition allows, that it is recorded, and that it is bounded.
/// </summary>
/// <param name="maxSteps">Upper bound on steps executed in one call. A cycle that never exits stops here.</param>
public sealed class GraphRunner<TState>(GraphDefinition<TState> graph, int maxSteps = 50, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly int _maxSteps = maxSteps > 0
        ? maxSteps
        : throw new ArgumentOutOfRangeException(nameof(maxSteps), maxSteps, "A run must be allowed at least one step.");

    public Task<GraphRun<TState>> RunAsync(TState state, CancellationToken cancellationToken) =>
        RunFromAsync(graph.Start, state, cancellationToken);

    /// <summary>Continues a paused run from the node its pause resumes at. Nothing before the pause runs again.</summary>
    public Task<GraphRun<TState>> ResumeAsync(string pause, TState state, CancellationToken cancellationToken) =>
        RunFromAsync(graph.ResumeTarget(pause), state, cancellationToken);

    private async Task<GraphRun<TState>> RunFromAsync(string first, TState state, CancellationToken cancellationToken)
    {
        using var activity = GraphTelemetry.StartRun(graph.Name);
        var trace = new List<NodeVisit>();
        var current = first;

        for (var steps = 0; steps < _maxSteps && graph.StopAt(current) is null; steps++)
        {
            var step = graph.Step(current);
            var visited = await ExecuteAsync(step, state, trace, cancellationToken).ConfigureAwait(false);
            if (visited.Error is not null)
            {
                return Finish(activity, new GraphRun<TState>(state, GraphRunStatus.Faulted, nameof(GraphRunStatus.Faulted), trace, visited.Error));
            }

            state = visited.State;
            current = step.Edge.Choose(step.Name, state);
        }

        return Finish(activity, graph.StopAt(current) is { } stop
            ? new GraphRun<TState>(state, stop, current, trace)
            : new GraphRun<TState>(state, GraphRunStatus.StepLimitReached, nameof(GraphRunStatus.StepLimitReached), trace));
    }

    private static GraphRun<TState> Finish(Activity? activity, GraphRun<TState> run)
    {
        activity?.SetTag("curverisk.graph.outcome", run.Outcome);
        if (run.Status is GraphRunStatus.Faulted or GraphRunStatus.StepLimitReached)
        {
            activity?.SetStatus(ActivityStatusCode.Error, run.Outcome);
        }

        return run;
    }

    private async Task<Visited> ExecuteAsync(Step<TState> step, TState state, List<NodeVisit> trace, CancellationToken cancellationToken)
    {
        if (step.Parallel is not { } parallel)
        {
            return Record(trace, await VisitAsync(new NamedNode<TState>(step.Name, step.Node!), state, cancellationToken).ConfigureAwait(false));
        }

        var branches = await Task.WhenAll(parallel.Nodes.Select(node => VisitAsync(node, state, cancellationToken))).ConfigureAwait(false);
        foreach (var branch in branches)
        {
            Record(trace, branch);
        }

        if (branches.FirstOrDefault(branch => branch.Error is not null) is { } failed)
        {
            return failed;
        }

        var join = GraphNode.FromRule<TState>(start => parallel.Merge(start, [.. branches.Select(branch => branch.State)]));
        return Record(trace, await VisitAsync(new NamedNode<TState>(step.Name, join), state, cancellationToken).ConfigureAwait(false));
    }

    private static Visited Record(List<NodeVisit> trace, Visited visited)
    {
        trace.Add(visited.Visit);
        return visited;
    }

    private async Task<Visited> VisitAsync(NamedNode<TState> node, TState state, CancellationToken cancellationToken)
    {
        using var activity = GraphTelemetry.StartNode(graph.Name, node.Name);
        var started = _time.GetTimestamp();
        try
        {
            var next = await node.Node.RunAsync(state, cancellationToken).ConfigureAwait(false);
            return new Visited(Visit(node.Name, NodeVisitResult.Completed, started), next, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Not swallowed: the exception travels in the run result, with the trace of how the run got here.
            // A cancellation the caller did not ask for (a timeout inside a node) is a failure like any other.
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            return new Visited(Visit(node.Name, NodeVisitResult.Failed, started), state, ex);
        }
    }

    private NodeVisit Visit(string node, NodeVisitResult result, long started) =>
        new(node, result, _time.GetElapsedTime(started).TotalMilliseconds);

    private sealed record Visited(NodeVisit Visit, TState State, Exception? Error);
}
