using CurveRisk.Workflows;

namespace CurveRisk.Ai.Tests.Workflows;

/// <summary>
/// A graph is checked as a whole before anything runs. Each test builds a graph that breaks exactly
/// one rule, which shows that the rule can fail and what it says when it does.
/// </summary>
public class GraphValidationTests
{
    [Fact]
    public void A_well_formed_graph_builds()
    {
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        Assert.Equal("test", graph.Name);
    }

    [Fact]
    public void A_graph_with_no_start_is_rejected()
    {
        var builder = new GraphBuilder<Tally>("test").AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done")).AddEnd("Done");

        Assert.Equal(["No start node was set."], ProblemsOf(builder));
    }

    [Fact]
    public void A_start_that_is_not_a_node_is_rejected()
    {
        var builder = new GraphBuilder<Tally>("test").StartAt("Done").AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done")).AddEnd("Done");

        Assert.Equal(["The start 'Done' is not a node."], ProblemsOf(builder));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Done")]
    [InlineData("start")]
    public void A_name_used_twice_is_rejected_including_the_reserved_start(string duplicate)
    {
        var builder = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done"))
            .AddNode(duplicate, Nodes.Logs("again"), Edge.To<Tally>("Done"))
            .AddEnd("Done");

        Assert.Contains($"The name '{duplicate}' is used more than once.", ProblemsOf(builder));
    }

    [Fact]
    public void An_edge_to_something_undeclared_is_rejected()
    {
        var builder = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(_ => "Done", new Branch("Done"), new Branch("nowhere", "never")))
            .AddEnd("Done");

        Assert.Equal(["'a' has an edge to 'nowhere', which is not declared."], ProblemsOf(builder));
    }

    [Fact]
    public void A_pause_that_resumes_at_something_other_than_a_node_is_rejected()
    {
        var builder = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("wait"))
            .AddPause("wait", resumeAt: "Done")
            .AddEnd("Done");

        Assert.Equal(["The pause 'wait' resumes at 'Done', which is not a node."], ProblemsOf(builder));
    }

    [Fact]
    public void A_node_with_no_way_out_is_rejected()
    {
        var builder = Nodes.Graph().AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(_ => "Done")).AddEnd("Done");

        Assert.Contains("'a' has no outgoing edge.", ProblemsOf(builder));
    }

    [Fact]
    public void A_parallel_step_with_no_branches_is_rejected()
    {
        var builder = Nodes.Graph()
            .AddParallel("a", new ParallelBranches<Tally>([], (start, _) => start), Edge.To<Tally>("Done"))
            .AddEnd("Done");

        Assert.Equal(["'a' is parallel but has no branches."], ProblemsOf(builder));
    }

    [Fact]
    public void A_node_and_an_end_that_nothing_leads_to_are_rejected()
    {
        var builder = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done"))
            .AddNode("orphan", Nodes.Logs("orphan"), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .AddEnd("Unused");

        Assert.Equal(["'orphan' cannot be reached from the start.", "'Unused' cannot be reached from the start."], ProblemsOf(builder));
    }

    [Fact]
    public void A_cycle_with_no_exit_is_rejected_along_with_the_path_into_it()
    {
        var builder = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(_ => "Done", new Branch("Done"), new Branch("b", "sometimes")))
            .AddNode("b", Nodes.Logs("b"), Edge.To<Tally>("c"))
            .AddNode("c", Nodes.Logs("c"), Edge.To<Tally>("b"))
            .AddEnd("Done");

        Assert.Equal(["No end can be reached from 'b'.", "No end can be reached from 'c'."], ProblemsOf(builder));
    }

    [Fact]
    public void A_cycle_that_can_exit_and_a_node_reached_only_by_resuming_are_accepted()
    {
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(_ => "wait", new Branch("a", "again"), new Branch("wait")))
            .AddPause("wait", resumeAt: "b")
            .AddNode("b", Nodes.Logs("b"), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        Assert.Equal(5, graph.Shape.Nodes.Count);
    }

    [Fact]
    public void Every_problem_is_reported_at_once_and_in_the_message()
    {
        var builder = new GraphBuilder<Tally>("test")
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("nowhere"))
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done"))
            .AddEnd("Done");

        var exception = Assert.Throws<GraphDefinitionException>(builder.Build);

        Assert.Equal(3, exception.Problems.Count);
        Assert.Equal(
            "The graph is not valid: The name 'a' is used more than once. 'a' has an edge to 'nowhere', which is not declared. No start node was set.",
            exception.Message);
    }

    private static IReadOnlyList<string> ProblemsOf(GraphBuilder<Tally> builder) =>
        Assert.Throws<GraphDefinitionException>(builder.Build).Problems;
}
