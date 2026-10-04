using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

/// <summary>One metric measurement: which instrument, what value, and the tags it carried.</summary>
internal sealed record CapturedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// Listens to the Copilot's spans and metrics for one test. Listeners are process-wide and tests run in
/// parallel, so the capture opens a root span of its own and keeps only what was recorded inside that
/// trace: telemetry from other tests is never seen.
/// </summary>
internal sealed class TelemetryCapture : IDisposable
{
    private readonly ActivitySource _rootSource = new("CurveRisk.Ai.Tests.TelemetryCapture");
    private readonly ActivityListener _activityListener = new();
    private readonly MeterListener _meterListener = new();
    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ConcurrentQueue<CapturedMeasurement> _measurements = new();
    private readonly Activity _root;

    public TelemetryCapture()
    {
        _activityListener.ShouldListenTo = source => source.Name == CopilotTelemetry.Name || ReferenceEquals(source, _rootSource);
        _activityListener.Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded;
        _activityListener.ActivityStopped = Keep;
        ActivitySource.AddActivityListener(_activityListener);
        _root = _rootSource.StartActivity("test")
            ?? throw new InvalidOperationException("The root span was not sampled.");

        _meterListener.InstrumentPublished = Subscribe;
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Keep(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Keep(instrument, value, tags));
        _meterListener.Start();
    }

    /// <summary>The Copilot's finished spans, in the order they ended.</summary>
    public IReadOnlyList<Activity> Spans => [.. _spans];

    public Activity Span(string name) => Assert.Single(_spans, span => span.OperationName == name);

    public IReadOnlyList<CapturedMeasurement> Measurements(string instrument) =>
        [.. _measurements.Where(measurement => measurement.Instrument == instrument)];

    public void Dispose()
    {
        _meterListener.Dispose();
        _root.Dispose();
        _activityListener.Dispose();
        _rootSource.Dispose();
    }

    private static void Subscribe(Instrument instrument, MeterListener listener)
    {
        if (instrument.Meter.Name == CopilotTelemetry.Name)
        {
            listener.EnableMeasurementEvents(instrument);
        }
    }

    private void Keep(Activity span)
    {
        if (span.Source.Name == CopilotTelemetry.Name && span.TraceId == _root.TraceId)
        {
            _spans.Enqueue(span);
        }
    }

    private void Keep(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        // Measurements are recorded synchronously inside the span they belong to.
        if (Activity.Current?.TraceId == _root.TraceId)
        {
            _measurements.Enqueue(new CapturedMeasurement(instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
        }
    }
}
