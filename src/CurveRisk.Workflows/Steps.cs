namespace CurveRisk.Workflows;

/// <summary>Nodes that run concurrently on the same input state, and how their results become one state again.</summary>
/// <param name="Merge">Receives the state the branches started from and each branch's result, in declaration order.</param>
public sealed record ParallelBranches<TState>(
    IReadOnlyList<NamedNode<TState>> Nodes,
    Func<TState, IReadOnlyList<TState>, TState> Merge);

/// <param name="Parallel">Null for an ordinary node.</param>
internal sealed record Step<TState>(string Name, IGraphNode<TState>? Node, ParallelBranches<TState>? Parallel, Edge<TState> Edge);

internal sealed record PausePoint(string Name, string ResumeAt);
