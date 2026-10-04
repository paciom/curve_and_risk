using System.Diagnostics;
using CurveRisk.Workflows;

namespace CurveRisk.Ai.Tests.Workflows;

public class GraphRunnerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nodes_run_in_edge_order_and_the_run_ends_at_the_named_end()
    {
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("b"))
            .AddNode("b", Nodes.Logs("b"), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct);

        Assert.Equal((GraphRunStatus.Ended, "Done", "a,b"), (run.Status, run.Outcome, run.State.Log));
        Assert.Equal(["a", "b"], run.Trace.Select(visit => visit.Node));
        Assert.All(run.Trace, visit => Assert.Equal(NodeVisitResult.Completed, visit.Result));
        Assert.Null(run.Error);
    }

    [Theory]
    [InlineData(0, "Small")]
    [InlineData(7, "Large")]
    public async Task A_route_is_chosen_from_the_state_the_node_returned(int start, string expected)
    {
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(tally => tally.Count > 5 ? "Large" : "Small", new Branch("Large"), new Branch("Small")))
            .AddEnd("Large")
            .AddEnd("Small")
            .Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(start), Ct);

        Assert.Equal(expected, run.Outcome);
    }

    [Fact]
    public async Task A_cycle_repeats_until_its_exit_condition_holds_and_each_pass_is_in_the_trace()
    {
        var run = await new GraphRunner<Tally>(Loop(exitAt: 3)).RunAsync(new Tally(), Ct);

        Assert.Equal((GraphRunStatus.Ended, 3), (run.Status, run.State.Count));
        Assert.Equal(["a", "a", "a"], run.Trace.Select(visit => visit.Node));
    }

    [Fact]
    public async Task A_cycle_that_does_not_exit_is_stopped_at_the_step_limit_with_a_named_status()
    {
        var run = await new GraphRunner<Tally>(Loop(exitAt: int.MaxValue), maxSteps: 4).RunAsync(new Tally(), Ct);

        Assert.Equal((GraphRunStatus.StepLimitReached, "StepLimitReached", 4), (run.Status, run.Outcome, run.Trace.Count));
    }

    [Fact]
    public async Task A_run_that_finishes_on_its_last_allowed_step_has_ended_not_hit_the_limit()
    {
        var run = await new GraphRunner<Tally>(Loop(exitAt: 4), maxSteps: 4).RunAsync(new Tally(), Ct);

        Assert.Equal((GraphRunStatus.Ended, "Done"), (run.Status, run.Outcome));
    }

    [Fact]
    public async Task Parallel_branches_start_from_the_same_state_and_are_merged_under_the_join()
    {
        var graph = Nodes.Graph()
            .AddParallel("a", Branches(Nodes.Logs("left"), Nodes.Logs("right")), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(10, "in"), Ct);

        Assert.Equal(new Tally(22, "in,left|in,right"), run.State);
        Assert.Equal(["left", "right", "a"], run.Trace.Select(visit => visit.Node));
    }

    [Fact]
    public async Task Parallel_branches_are_in_flight_at_the_same_time()
    {
        var (leftEntered, rightEntered) = (new TaskCompletionSource(), new TaskCompletionSource());
        var graph = Nodes.Graph()
            .AddParallel(
                "a",
                // Each branch can only finish once the other has started: sequential execution would never complete.
                Branches(new GatedNode("left", leftEntered, rightEntered.Task), new GatedNode("right", rightEntered, leftEntered.Task)),
                Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(GraphRunStatus.Ended, run.Status);
    }

    [Fact]
    public async Task A_node_that_throws_faults_the_run_and_the_error_and_the_trace_come_back()
    {
        var failure = new InvalidOperationException("boom");
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("b"))
            .AddNode("b", Nodes.Throws(failure), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct);

        Assert.Equal((GraphRunStatus.Faulted, "Faulted"), (run.Status, run.Outcome));
        Assert.Same(failure, run.Error);
        Assert.Equal("a", run.State.Log);
        Assert.Equal([new("a", NodeVisitResult.Completed), new("b", NodeVisitResult.Failed)], run.Trace.Select(v => (v.Node, v.Result)));
    }

    [Fact]
    public async Task A_parallel_branch_that_throws_faults_the_run_and_the_join_does_not_happen()
    {
        var graph = Nodes.Graph()
            .AddParallel("a", Branches(Nodes.Logs("left"), Nodes.Throws(new TimeoutException())), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        var run = await new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct);

        Assert.IsType<TimeoutException>(run.Error);
        Assert.Equal(new Tally(), run.State);
        Assert.Equal([new("left", NodeVisitResult.Completed), new("right", NodeVisitResult.Failed)], run.Trace.Select(v => (v.Node, v.Result)));
    }

    [Fact]
    public async Task Cancellation_is_not_a_fault_it_stops_the_run()
    {
        using var cancelled = new CancellationTokenSource();
        var graph = Nodes.Graph()
            .AddNode("a", new GatedNode("a", new TaskCompletionSource(), Task.Delay(Timeout.Infinite, cancelled.Token)), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        var running = new GraphRunner<Tally>(graph).RunAsync(new Tally(), cancelled.Token);
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task A_route_that_returns_an_undeclared_target_is_a_programming_error_not_a_path()
    {
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(_ => "Elsewhere", new Branch("Done")))
            .AddEnd("Done")
            .Build();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct));

        Assert.Equal("'a' routed to 'Elsewhere', which is not one of its declared branches.", error.Message);
    }

    [Fact]
    public async Task A_pause_hands_back_the_state_and_resuming_runs_only_what_follows_it()
    {
        var runner = new GraphRunner<Tally>(PausingGraph());

        var paused = await runner.RunAsync(new Tally(), Ct);
        var resumed = await runner.ResumeAsync(paused.Outcome, paused.State, Ct);

        Assert.Equal((GraphRunStatus.Paused, "wait", "a"), (paused.Status, paused.Outcome, paused.State.Log));
        Assert.Equal((GraphRunStatus.Ended, "Done", "a,b"), (resumed.Status, resumed.Outcome, resumed.State.Log));
        Assert.Equal(["b"], resumed.Trace.Select(visit => visit.Node));
    }

    [Fact]
    public async Task Resuming_at_something_that_is_not_a_pause_is_refused()
    {
        var runner = new GraphRunner<Tally>(PausingGraph());

        var error = await Assert.ThrowsAsync<ArgumentException>(() => runner.ResumeAsync("b", new Tally(), Ct));

        Assert.StartsWith("'b' is not a pause in the graph 'test'.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_visit_records_how_long_the_node_took_on_the_injected_clock()
    {
        var clock = new ManualStopwatch();
        var slow = GraphNode.FromRule<Tally>(tally =>
        {
            clock.Advance(250);
            return tally;
        });
        var graph = Nodes.Graph().AddNode("a", slow, Edge.To<Tally>("Done")).AddEnd("Done").Build();

        var run = await new GraphRunner<Tally>(graph, timeProvider: clock).RunAsync(new Tally(), Ct);

        Assert.Equal(new NodeVisit("a", NodeVisitResult.Completed, 250), Assert.Single(run.Trace));
    }

    [Fact]
    public async Task A_run_and_each_node_have_a_span_and_a_failure_marks_both()
    {
        using var spans = new GraphSpans();
        var graph = Nodes.Graph()
            .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("b"))
            .AddNode("b", Nodes.Throws(new TimeoutException("secret")), Edge.To<Tally>("Done"))
            .AddEnd("Done")
            .Build();

        await new GraphRunner<Tally>(graph).RunAsync(new Tally(), Ct);

        var run = spans.Span("run_graph test");
        Assert.Equal(("test", "Faulted", ActivityStatusCode.Error), (run.GetTagItem("curverisk.graph.name"), run.GetTagItem("curverisk.graph.outcome"), run.Status));
        Assert.Equal(("a", ActivityStatusCode.Unset), (spans.Span("run_node a").GetTagItem("curverisk.graph.node"), spans.Span("run_node a").Status));

        // The exception's type is recorded, never its message: messages can carry data.
        Assert.Equal((ActivityStatusCode.Error, "TimeoutException"), (spans.Span("run_node b").Status, spans.Span("run_node b").StatusDescription));
    }

    [Fact]
    public async Task A_run_that_ends_normally_leaves_its_span_unmarked()
    {
        using var spans = new GraphSpans();

        await new GraphRunner<Tally>(Loop(exitAt: 1)).RunAsync(new Tally(), Ct);
        var ended = spans.Span("run_graph test");
        Assert.Equal(("Done", ActivityStatusCode.Unset), (ended.GetTagItem("curverisk.graph.outcome"), ended.Status));
    }

    [Fact]
    public async Task Hitting_the_step_limit_marks_the_run_span_as_an_error()
    {
        using var spans = new GraphSpans();

        await new GraphRunner<Tally>(Loop(exitAt: int.MaxValue), maxSteps: 2).RunAsync(new Tally(), Ct);

        Assert.Equal(ActivityStatusCode.Error, spans.Span("run_graph test").Status);
    }

    private static GraphDefinition<Tally> Loop(int exitAt) => Nodes.Graph()
        .AddNode("a", Nodes.Logs("a"), Edge.Route<Tally>(tally => tally.Count >= exitAt ? "Done" : "a", new Branch("a", "again"), new Branch("Done")))
        .AddEnd("Done")
        .Build();

    private static GraphDefinition<Tally> PausingGraph() => Nodes.Graph()
        .AddNode("a", Nodes.Logs("a"), Edge.To<Tally>("wait"))
        .AddPause("wait", resumeAt: "b")
        .AddNode("b", Nodes.Logs("b"), Edge.To<Tally>("Done"))
        .AddEnd("Done")
        .Build();

    private static ParallelBranches<Tally> Branches(IGraphNode<Tally> left, IGraphNode<Tally> right) => new(
        [new NamedNode<Tally>("left", left), new NamedNode<Tally>("right", right)],
        (start, results) => new Tally(results.Sum(result => result.Count), string.Join('|', results.Select(result => result.Log))));
}
