namespace CurveRisk.Workflows;

public enum GraphRunStatus
{
    /// <summary>The run reached an end. <see cref="GraphRun{TState}.Outcome"/> is that end's name.</summary>
    Ended,

    /// <summary>The run is waiting at a pause. <see cref="GraphRun{TState}.Outcome"/> is the pause's name.</summary>
    Paused,

    /// <summary>The run was stopped because it executed more nodes than the runner allows.</summary>
    StepLimitReached,

    /// <summary>A node threw. The state is the one that node was given.</summary>
    Faulted,
}

public enum NodeVisitResult
{
    Completed,
    Failed,
}

/// <summary>One execution of one node. A node inside a cycle appears once per execution.</summary>
public sealed record NodeVisit(string Node, NodeVisitResult Result, double DurationMs);

/// <param name="Outcome">The end or pause reached; otherwise the name of the status.</param>
/// <param name="Trace">Every node executed, in order. Parallel branches are listed in declaration order, then their join.</param>
/// <param name="Error">What a node threw, when <see cref="Status"/> is Faulted.</param>
public sealed record GraphRun<TState>(
    TState State,
    GraphRunStatus Status,
    string Outcome,
    IReadOnlyList<NodeVisit> Trace,
    Exception? Error = null);
