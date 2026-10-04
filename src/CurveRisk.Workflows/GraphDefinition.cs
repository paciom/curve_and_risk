namespace CurveRisk.Workflows;

internal sealed record GraphStops(IReadOnlyList<PausePoint> Pauses, IReadOnlyList<string> Ends);

/// <summary>A validated graph. It is immutable and holds no run state, so one instance serves concurrent runs.</summary>
public sealed class GraphDefinition<TState>
{
    private readonly Dictionary<string, Step<TState>> _steps;
    private readonly GraphStops _stops;

    internal GraphDefinition(string name, string start, IReadOnlyList<Step<TState>> steps, GraphStops stops)
    {
        Name = name;
        Start = start;
        _steps = steps.ToDictionary(step => step.Name, StringComparer.Ordinal);
        _stops = stops;
        Shape = new GraphShape(name, [.. ShapeNodes(steps)], [.. ShapeEdges(steps)]);
    }

    public string Name { get; }

    public GraphShape Shape { get; }

    internal string Start { get; }

    internal Step<TState> Step(string name) => _steps[name];

    /// <summary>Ended or Paused when <paramref name="name"/> is where a run stops; null when it is a node to execute.</summary>
    internal GraphRunStatus? StopAt(string name)
    {
        if (_stops.Ends.Contains(name, StringComparer.Ordinal))
        {
            return GraphRunStatus.Ended;
        }

        return _stops.Pauses.Any(pause => pause.Name == name) ? GraphRunStatus.Paused : null;
    }

    internal string ResumeTarget(string pause) =>
        _stops.Pauses.FirstOrDefault(point => point.Name == pause)?.ResumeAt
        ?? throw new ArgumentException($"'{pause}' is not a pause in the graph '{Name}'.", nameof(pause));

    private IEnumerable<GraphShapeNode> ShapeNodes(IReadOnlyList<Step<TState>> steps)
    {
        yield return new GraphShapeNode(GraphShape.StartName, GraphNodeKind.Start);
        foreach (var step in steps)
        {
            foreach (var branch in step.Parallel?.Nodes ?? [])
            {
                yield return new GraphShapeNode(branch.Name, GraphNodeKind.ParallelBranch);
            }

            yield return new GraphShapeNode(step.Name, step.Parallel is null ? GraphNodeKind.Node : GraphNodeKind.Join);
        }

        foreach (var pause in _stops.Pauses)
        {
            yield return new GraphShapeNode(pause.Name, GraphNodeKind.Pause);
        }

        foreach (var end in _stops.Ends)
        {
            yield return new GraphShapeNode(end, GraphNodeKind.End);
        }
    }

    private IEnumerable<GraphShapeEdge> ShapeEdges(IReadOnlyList<Step<TState>> steps)
    {
        var edges = Entries(Start).Select(entry => new GraphShapeEdge(GraphShape.StartName, entry, ""));
        foreach (var step in steps)
        {
            edges = edges
                .Concat((step.Parallel?.Nodes ?? []).Select(branch => new GraphShapeEdge(branch.Name, step.Name, "")))
                .Concat(step.Edge.Branches.SelectMany(branch =>
                    Entries(branch.Target).Select(entry => new GraphShapeEdge(step.Name, entry, branch.When))));
        }

        return edges.Concat(_stops.Pauses.SelectMany(pause =>
            Entries(pause.ResumeAt).Select(entry => new GraphShapeEdge(pause.Name, entry, "resume"))));
    }

    /// <summary>An edge into a parallel step is drawn into each of its branches; the join is where they meet again.</summary>
    private IEnumerable<string> Entries(string target) =>
        _steps.TryGetValue(target, out var step) && step.Parallel is { } parallel
            ? parallel.Nodes.Select(node => node.Name)
            : [target];
}
