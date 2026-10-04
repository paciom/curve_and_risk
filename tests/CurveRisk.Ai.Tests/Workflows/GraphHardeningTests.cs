using CurveRisk.Workflows;

namespace CurveRisk.Ai.Tests.Workflows;

/// <summary>Limits and edge cases of the runtime that an independent review found unguarded.</summary>
public class GraphHardeningTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_runner_that_may_take_no_steps_is_refused(int maxSteps)
    {
        var graph = Nodes.Graph().AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("Done")).AddEnd("Done").Build();

        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphRunner<Tally>(graph, maxSteps));
    }

    [Fact]
    public async Task A_cancellation_the_caller_did_not_ask_for_is_a_fault_with_a_trace()
    {
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Throws(new TaskCanceledException("timed out")), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct);

        Assert.Equal(GraphRunStatus.Faulted, run.Status);
        Assert.IsType<TaskCanceledException>(run.Error);
        Assert.Equal(NodeVisitResult.Failed, Assert.Single(run.Trace).Result);
    }

    [Fact]
    public async Task A_merge_that_throws_faults_the_run_at_the_join()
    {
        var branches = new ParallelBranches<Tally>(
            [new NamedNode<Tally>("left", Nodes.Logs("left"))],
            (_, _) => throw new InvalidOperationException("cannot merge"));
        var graph = Nodes.Graph().AddParallel("a", branches, Edge.To<Tally>("Done")).AddEnd("Done").Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct);

        Assert.Equal(GraphRunStatus.Faulted, run.Status);
        Assert.Equal([new("left", NodeVisitResult.Completed), new("a", NodeVisitResult.Failed)], run.Trace.Select(v => (v.Node, v.Result)));
    }

    [Theory]
    [InlineData("load book")]
    [InlineData("end")]
    [InlineData("9lives")]
    [InlineData("a|b")]
    public void A_name_that_would_need_escaping_in_a_diagram_is_rejected(string name)
    {
        var builder = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>(name))
            .AddNode(name, Nodes.Logs("b"), Edge.To<Tally>("Done"))
            .AddEnd("Done");

        var problems = Assert.Throws<GraphDefinitionException>(builder.Build).Problems;

        Assert.Contains($"The name '{name}' is not usable: use letters, digits and underscores, starting with a letter, and not 'end'.", problems);
    }

    [Theory]
    [InlineData("a|b")]
    [InlineData("say \"yes\"")]
    [InlineData("two\nlines")]
    public void An_edge_label_with_diagram_syntax_in_it_is_rejected(string label)
    {
        var builder = Nodes.Graph().AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(_ => "Done", new Branch("Done", label))).AddEnd("Done");

        var problem = Assert.Single(Assert.Throws<GraphDefinitionException>(builder.Build).Problems);

        Assert.EndsWith("a label may not contain '|', a quote or a line break.", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parallel_branch_may_not_share_a_name_with_anything_else()
    {
        var branches = new ParallelBranches<Tally>([new NamedNode<Tally>("Done", Nodes.Logs("x"))], (start, _) => start);
        var builder = Nodes.Graph().AddParallel("a", branches, Edge.To<Tally>("Done")).AddEnd("Done");

        Assert.Contains("The name 'Done' is used more than once.", Assert.Throws<GraphDefinitionException>(builder.Build).Problems);
    }

    [Fact]
    public void An_edge_may_not_point_into_the_middle_of_a_parallel_step()
    {
        var branches = new ParallelBranches<Tally>([new NamedNode<Tally>("left", Nodes.Logs("left"))], (start, _) => start);
        var builder = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(_ => "fan", new Branch("fan"), new Branch("left", "skip the others")))
            .AddParallel("fan", branches, Edge.To<Tally>("Done"))
            .AddEnd("Done");

        Assert.Equal(["'a' has an edge to 'left', which is not declared."], Assert.Throws<GraphDefinitionException>(builder.Build).Problems);
    }

    [Fact]
    public void A_store_that_can_hold_nothing_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryCheckpointStore<string>(new CheckpointLimits(0, TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public void A_checkpoint_can_be_taken_until_its_lifetime_is_over_and_not_after()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryCheckpointStore<string>(new CheckpointLimits(10, TimeSpan.FromMinutes(15)), clock);
        var inTime = store.Save("in time");
        var late = store.Save("late");

        clock.Now += TimeSpan.FromMinutes(15);
        var taken = store.Take(inTime);
        clock.Now += TimeSpan.FromSeconds(1);

        Assert.Equal("in time", taken);
        Assert.Null(store.Take(late));
    }

    [Fact]
    public async Task Two_decisions_racing_for_one_checkpoint_cannot_both_get_it()
    {
        var store = new InMemoryCheckpointStore<string>();
        var id = store.Save("state");
        using var start = new ManualResetEventSlim();

        var takers = Enumerable.Range(0, 16).Select(_ => Task.Run(
            () =>
            {
                start.Wait(Ct);
                return store.Take(id);
            },
            Ct)).ToArray();
        start.Set();

        Assert.Single(await Task.WhenAll(takers), taken => taken is not null);
    }
}
