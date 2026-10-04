using CurveRisk.Copilot.Briefs;
using CurveRisk.Workflows;

namespace CurveRisk.Ai.Tests.Briefs;

/// <summary>
/// The diagram in the documentation is the one the code generates. If the graph changes and the page
/// does not, this fails and prints the diagram to paste.
/// </summary>
public class RiskBriefDiagramTests
{
    private const string Document = "docs/ai-engineering.md";

    [Fact]
    public void The_documented_diagram_is_exactly_what_the_graph_definition_exports()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), Document)).ReplaceLineEndings("\n");
        const string open = "```mermaid\n";
        var start = text.IndexOf(open, StringComparison.Ordinal) + open.Length;
        var documented = text[start..text.IndexOf("```", start, StringComparison.Ordinal)];

        Assert.Equal(RiskBriefWorkflow.Shape.ToMermaid(), documented);
    }

    [Fact]
    public void The_graph_has_one_model_node_one_pause_and_names_every_way_a_brief_can_end()
    {
        var shape = RiskBriefWorkflow.Shape;

        Assert.Equal(["await_approval"], Names(shape, GraphNodeKind.Pause));
        Assert.Equal(["price_trades", "run_risk", "run_scenarios"], Names(shape, GraphNodeKind.ParallelBranch));
        Assert.Equal(
            ["Completed", "WithoutCommentary", "EmptyPortfolio", "EngineFailed", "ScenarioSaved", "SaveDeclined", "SaveFailed"],
            Names(shape, GraphNodeKind.End));

        // The only way into the save step is by resuming from the pause.
        Assert.Equal(new GraphShapeEdge("await_approval", "save_scenario", "resume"), Assert.Single(shape.Edges, edge => edge.To == "save_scenario"));
    }

    private static IEnumerable<string> Names(GraphShape shape, GraphNodeKind kind) =>
        shape.Nodes.Where(node => node.Kind == kind).Select(node => node.Name);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CurveRisk.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CurveRisk.slnx was not found above the test directory.");
    }
}
