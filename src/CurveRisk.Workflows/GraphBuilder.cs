namespace CurveRisk.Workflows;

/// <summary>The graph is not valid. Every problem found is listed, not only the first.</summary>
public sealed class GraphDefinitionException(IReadOnlyList<string> problems)
    : Exception($"The graph is not valid: {string.Join(" ", problems)}")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Collects nodes, edges, pauses and ends, then validates them as a whole. Nothing runs until
/// <see cref="Build"/> has accepted the graph, so a wiring mistake is found at start-up, not mid-run.
/// </summary>
public sealed class GraphBuilder<TState>(string name)
{
    private readonly List<Step<TState>> _steps = [];
    private readonly List<PausePoint> _pauses = [];
    private readonly List<string> _ends = [];
    private string _start = "";

    public GraphBuilder<TState> StartAt(string node)
    {
        _start = node;
        return this;
    }

    public GraphBuilder<TState> AddNode(string node, IGraphNode<TState> implementation, Edge<TState> edge)
    {
        _steps.Add(new Step<TState>(node, implementation, null, edge));
        return this;
    }

    /// <summary>Runs the branches concurrently, merges their results under <paramref name="join"/>, then follows the edge.</summary>
    public GraphBuilder<TState> AddParallel(string join, ParallelBranches<TState> branches, Edge<TState> edge)
    {
        _steps.Add(new Step<TState>(join, null, branches, edge));
        return this;
    }

    /// <summary>A point where the run stops and hands back its state. Resuming continues at <paramref name="resumeAt"/>.</summary>
    public GraphBuilder<TState> AddPause(string pause, string resumeAt)
    {
        _pauses.Add(new PausePoint(pause, resumeAt));
        return this;
    }

    public GraphBuilder<TState> AddEnd(string outcome)
    {
        _ends.Add(outcome);
        return this;
    }

    public GraphDefinition<TState> Build()
    {
        var problems = GraphValidator.Validate(new GraphLayout(_start, [.. _steps.Select(Outline)], _pauses, _ends));
        return problems.Count == 0
            ? new GraphDefinition<TState>(name, _start, [.. _steps], new GraphStops([.. _pauses], [.. _ends]))
            : throw new GraphDefinitionException(problems);
    }

    private static StepOutline Outline(Step<TState> step) => new(
        step.Name,
        [.. step.Edge.Branches.Select(branch => branch.Target)],
        step.Parallel is null ? null : [.. step.Parallel.Nodes.Select(node => node.Name)],
        [.. step.Edge.Branches.Select(branch => branch.When)]);
}
