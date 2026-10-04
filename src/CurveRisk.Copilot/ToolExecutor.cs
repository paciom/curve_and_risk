using System.Text.Json;
using CurveRisk.Ai.Tools;

namespace CurveRisk.Copilot;

/// <summary>What happened to one tool call: the record for the caller and the result to send back to the model.</summary>
public sealed record ExecutedTool(ToolInvocation Invocation, ToolResultPart Result);

/// <summary>
/// Runs the tool calls a model asks for. This is where "writes need a human" is enforced: a tool
/// registered as needing approval does not run unless the gate says yes.
/// </summary>
public sealed class ToolExecutor(IReadOnlyList<CopilotTool> tools, IApprovalGate approvals)
{
    private readonly Dictionary<string, CopilotTool> _byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>In catalog order. A stable tool list is a precondition for prompt-cache hits.</summary>
    public IReadOnlyList<ToolSpec> Specs { get; } =
        [.. tools.Select(t => new ToolSpec(t.Name, t.Function.Description, t.Function.JsonSchema))];

    public async Task<IReadOnlyList<ExecutedTool>> ExecuteAsync(IReadOnlyList<ToolCallPart> calls, CancellationToken cancellationToken)
    {
        // Approvals are asked one at a time so a person is never shown overlapping prompts...
        var denials = await CollectDenialsAsync(calls, cancellationToken).ConfigureAwait(false);

        // ...then everything that may run, runs concurrently. Results keep the order of the calls.
        return await Task
            .WhenAll(calls.Select(call => RunAsync(call, denials.GetValueOrDefault(call.Id), cancellationToken)))
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> CollectDenialsAsync(IReadOnlyList<ToolCallPart> calls, CancellationToken cancellationToken)
    {
        var denials = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var call in calls.Where(c => _byName.TryGetValue(c.Name, out var tool) && tool.RequiresApproval))
        {
            var decision = await approvals.RequestAsync(call, cancellationToken).ConfigureAwait(false);
            if (!decision.Approved)
            {
                denials[call.Id] = decision.Reason;
            }
        }

        return denials;
    }

    private async Task<ExecutedTool> RunAsync(ToolCallPart call, string? denialReason, CancellationToken cancellationToken)
    {
        using var activity = CopilotTelemetry.StartTool(call);
        var (content, outcome) = await InvokeAsync(call, denialReason, cancellationToken).ConfigureAwait(false);
        CopilotTelemetry.RecordTool(activity, call.Name, outcome);

        return new ExecutedTool(
            new ToolInvocation(call.Name, call.Input, content, outcome),
            new ToolResultPart(call.Id, content, IsError: outcome != ToolOutcome.Succeeded));
    }

    private async Task<(string Content, ToolOutcome Outcome)> InvokeAsync(
        ToolCallPart call,
        string? denialReason,
        CancellationToken cancellationToken)
    {
        if (!_byName.TryGetValue(call.Name, out var tool))
        {
            return ($"Unknown tool '{call.Name}'.", ToolOutcome.UnknownTool);
        }

        if (denialReason is not null)
        {
            return ($"Not executed. {denialReason}", ToolOutcome.Denied);
        }

        try
        {
            return (await ToolCatalog.InvokeAsync(tool, call.Input, cancellationToken).ConfigureAwait(false), ToolOutcome.Succeeded);
        }
        catch (RiskEngineException ex)
        {
            return (ex.Message, ToolOutcome.Failed);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException or InvalidOperationException)
        {
            // Malformed arguments from the model. Give it enough to correct itself, nothing internal.
            return ($"Invalid arguments for '{call.Name}': {ex.Message}", ToolOutcome.Failed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One tool failing must not abandon its siblings: an approved write may already have run,
            // and the caller needs the record of it. Internal detail stays out of the model's view.
            return ($"'{call.Name}' failed unexpectedly.", ToolOutcome.Failed);
        }
    }
}
