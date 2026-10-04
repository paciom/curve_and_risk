using System.Text;

namespace CurveRisk.Workflows;

public enum GraphNodeKind
{
    Start,
    Node,

    /// <summary>One of several nodes that run at the same time.</summary>
    ParallelBranch,

    /// <summary>Where parallel branches are merged back into one state.</summary>
    Join,

    /// <summary>The run stops here and waits to be resumed.</summary>
    Pause,
    End,
}

public sealed record GraphShapeNode(string Name, GraphNodeKind Kind);

public sealed record GraphShapeEdge(string From, string To, string Label);

/// <summary>
/// The structure of a graph with nothing executable in it: what a diagram, a web page or a document
/// needs. It is derived from the definition that runs, so a picture made from it cannot drift from the code.
/// </summary>
public sealed record GraphShape(string Name, IReadOnlyList<GraphShapeNode> Nodes, IReadOnlyList<GraphShapeEdge> Edges)
{
    public const string StartName = "start";

    /// <summary>A Mermaid flowchart. Nodes and edges appear in declaration order, so the text is stable.</summary>
    public string ToMermaid()
    {
        var text = new StringBuilder("flowchart TD\n");
        foreach (var node in Nodes)
        {
            text.Append("    ").Append(Declare(node)).Append('\n');
        }

        foreach (var edge in Edges)
        {
            var arrow = edge.Label.Length == 0 ? "-->" : $"-->|{edge.Label}|";
            text.Append("    ").Append(edge.From).Append(' ').Append(arrow).Append(' ').Append(edge.To).Append('\n');
        }

        return text.ToString();
    }

    private static string Declare(GraphShapeNode node) => node.Kind switch
    {
        GraphNodeKind.Start or GraphNodeKind.End => $"{node.Name}([{node.Name}])",
        GraphNodeKind.Join => $"{node.Name}{{{{{node.Name}}}}}",
        GraphNodeKind.Pause => $"{node.Name}[/{node.Name}/]",
        _ => $"{node.Name}[{node.Name}]",
    };
}
