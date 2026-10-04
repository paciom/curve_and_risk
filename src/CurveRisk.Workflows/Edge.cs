namespace CurveRisk.Workflows;

/// <param name="Target">A node, a pause or an end.</param>
/// <param name="When">The condition in words, for the diagram. Empty for an unconditional edge.</param>
public sealed record Branch(string Target, string When = "");

/// <summary>
/// What may follow a node. Every possible target is declared up front, so the graph can be validated
/// and drawn without running it; the choice between them is made from the state at run time.
/// </summary>
public sealed class Edge<TState>
{
    private readonly Func<TState, string> _choose;

    internal Edge(Func<TState, string> choose, IReadOnlyList<Branch> branches)
    {
        _choose = choose;
        Branches = branches;
    }

    public IReadOnlyList<Branch> Branches { get; }

    internal string Choose(string from, TState state)
    {
        var target = _choose(state);
        return Branches.Any(branch => string.Equals(branch.Target, target, StringComparison.Ordinal))
            ? target
            : throw new InvalidOperationException($"'{from}' routed to '{target}', which is not one of its declared branches.");
    }
}

public static class Edge
{
    public static Edge<TState> To<TState>(string target) => new(_ => target, [new Branch(target)]);

    /// <param name="choose">Returns the target to take. It must be one of <paramref name="branches"/>.</param>
    public static Edge<TState> Route<TState>(Func<TState, string> choose, params Branch[] branches) => new(choose, branches);
}
