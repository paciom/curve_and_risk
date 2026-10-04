namespace CurveRisk.Workflows;

/// <summary>
/// One step of a graph. A node takes the state, does its one job and returns the next state; it never
/// decides what runs after it. That decision belongs to the edges, which is the point of a graph.
/// </summary>
public interface IGraphNode<TState>
{
    Task<TState> RunAsync(TState state, CancellationToken cancellationToken);
}

/// <summary>A node together with the name it has in the graph, its trace and its diagram.</summary>
public sealed record NamedNode<TState>(string Name, IGraphNode<TState> Node);

public static class GraphNode
{
    /// <summary>A node that is a pure function of the state: a rule, a check, a mapping. No I/O, no model.</summary>
    public static IGraphNode<TState> FromRule<TState>(Func<TState, TState> rule) => new RuleNode<TState>(rule);

    private sealed class RuleNode<TState>(Func<TState, TState> rule) : IGraphNode<TState>
    {
        public Task<TState> RunAsync(TState state, CancellationToken cancellationToken) => Task.FromResult(rule(state));
    }
}
