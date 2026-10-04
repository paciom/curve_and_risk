using System.Collections.Concurrent;
using System.Diagnostics;
using CurveRisk.Workflows;

namespace CurveRisk.Ai.Tests.Workflows;

/// <summary>The state the runtime tests pass round: a number to route on and a log of which nodes ran.</summary>
internal sealed record Tally(int Count = 0, string Log = "")
{
    public Tally Ran(string node) => this with { Count = Count + 1, Log = Log.Length == 0 ? node : $"{Log},{node}" };
}

internal static class Nodes
{
    public static IGraphNode<Tally> Logs(string name) => GraphNode.FromRule<Tally>(tally => tally.Ran(name));

    public static IGraphNode<Tally> Throws(Exception exception) => GraphNode.FromRule<Tally>(_ => throw exception);

    public static GraphBuilder<Tally> Graph() => new GraphBuilder<Tally>("test").StartAt("a");
}

/// <summary>A node that does not finish until it is released, so a test can see what else runs meanwhile.</summary>
internal sealed class GatedNode(string name, TaskCompletionSource entered, Task release) : IGraphNode<Tally>
{
    public async Task<Tally> RunAsync(Tally state, CancellationToken cancellationToken)
    {
        entered.SetResult();
        await release.WaitAsync(cancellationToken);
        return state.Ran(name);
    }
}

/// <summary>A clock whose stopwatch only moves when a test moves it.</summary>
internal sealed class ManualStopwatch : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _ticks;

    public void Advance(int milliseconds) => _ticks += milliseconds;
}

/// <summary>
/// The graph runtime's spans for one test. Listeners are process-wide and tests run in parallel, so
/// only spans inside this capture's own root trace are kept.
/// </summary>
internal sealed class GraphSpans : IDisposable
{
    private readonly ActivitySource _rootSource = new("CurveRisk.Ai.Tests.GraphSpans");
    private readonly ActivityListener _listener = new();
    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly Activity _root;

    public GraphSpans()
    {
        _listener.ShouldListenTo = source => source.Name == GraphTelemetry.Name || ReferenceEquals(source, _rootSource);
        _listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded;
        ActivitySource.AddActivityListener(_listener);
        _root = _rootSource.StartActivity("test") ?? throw new InvalidOperationException("The root span was not sampled.");
        _listener.ActivityStopped = span =>
        {
            if (span.Source.Name == GraphTelemetry.Name && span.TraceId == _root.TraceId)
            {
                _spans.Enqueue(span);
            }
        };
    }

    public Activity Span(string name) => Assert.Single(_spans, span => span.OperationName == name);

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
        _rootSource.Dispose();
    }
}
