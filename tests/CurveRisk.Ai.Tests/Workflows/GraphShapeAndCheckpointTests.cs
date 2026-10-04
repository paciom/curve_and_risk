using CurveRisk.Workflows;

namespace CurveRisk.Ai.Tests.Workflows;

public class GraphShapeTests
{
    [Fact]
    public void The_diagram_is_generated_from_the_definition_with_every_kind_of_node_and_edge()
    {
        var branches = new ParallelBranches<Tally>(
            [new NamedNode<Tally>("left", Nodes.Logs("left")), new NamedNode<Tally>("right", Nodes.Logs("right"))],
            (start, _) => start);
        var graph = new GraphBuilder<Tally>("test").StartAt("fan")
            .AddParallel("fan", branches, Edge.Route<Tally>(_ => "wait", new Branch("Failed", "a branch failed"), new Branch("wait")))
            .AddPause("wait", resumeAt: "fan")
            .AddEnd("Failed")
            .Build();

        const string expected = """
            flowchart TD
                start([start])
                left[left]
                right[right]
                fan{{fan}}
                wait[/wait/]
                Failed([Failed])
                start --> left
                start --> right
                left --> fan
                right --> fan
                fan -->|a branch failed| Failed
                fan --> wait
                wait -->|resume| left
                wait -->|resume| right

            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), graph.Shape.ToMermaid());
    }

    [Fact]
    public void The_shape_names_each_node_with_its_kind()
    {
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        Assert.Equal(
            [new("start", GraphNodeKind.Start), new("a", GraphNodeKind.Node), new("Done", GraphNodeKind.End)],
            graph.Shape.Nodes);
        Assert.Equal([new("start", "a", ""), new("a", "Done", "")], graph.Shape.Edges);
    }
}

public class CheckpointStoreTests
{
    [Fact]
    public void A_checkpoint_can_be_taken_once_with_the_id_it_was_saved_under()
    {
        var store = new InMemoryCheckpointStore<string>();

        var id = store.Save("state");

        Assert.Equal("state", store.Take(id));
        Assert.Null(store.Take(id));
    }

    [Fact]
    public void An_unknown_id_takes_nothing()
    {
        var store = new InMemoryCheckpointStore<string>();
        store.Save("state");

        Assert.Null(store.Take("not-an-id"));
    }

    [Fact]
    public void Ids_are_long_random_and_never_repeat()
    {
        var store = new InMemoryCheckpointStore<string>();

        var ids = Enumerable.Range(0, 50).Select(_ => store.Save("state")).ToList();

        Assert.Equal(50, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{32}$", id));
    }

    [Fact]
    public void Only_the_most_recent_checkpoints_are_kept_so_abandoned_runs_cannot_fill_memory()
    {
        var store = new InMemoryCheckpointStore<string>(new CheckpointLimits(2, TimeSpan.FromMinutes(15)));

        var first = store.Save("first");
        var second = store.Save("second");
        var third = store.Save("third");

        Assert.Null(store.Take(first));
        Assert.Equal(("second", "third"), (store.Take(second), store.Take(third)));
    }

    [Fact]
    public void Taking_a_checkpoint_does_not_let_older_ones_outlive_the_capacity()
    {
        var store = new InMemoryCheckpointStore<string>(new CheckpointLimits(2, TimeSpan.FromMinutes(15)));

        var first = store.Save("first");
        store.Take(store.Save("second"));
        store.Save("third");

        Assert.Null(store.Take(first));
    }
}
