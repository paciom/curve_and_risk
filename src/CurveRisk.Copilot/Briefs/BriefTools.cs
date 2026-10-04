using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Workflows;

namespace CurveRisk.Copilot.Briefs;

/// <summary>The outcome of one batch of calls to the same tool, with its successful results read back as <typeparamref name="T"/>.</summary>
internal sealed record ToolBatch<T>(IReadOnlyList<ExecutedTool> Executed)
{
    public IReadOnlyList<T> Results { get; } =
    [
        .. Executed.Where(tool => !tool.Result.IsError).Select(tool => JsonSerializer.Deserialize<T>(tool.Result.Content, JsonSerializerOptions.Web)!),
    ];

    /// <summary>The only result of a one-call batch. Default when that call failed.</summary>
    public T? Single => Results.Count > 0 ? Results[0] : default;

    /// <summary>The first failure, in words. Null when every call succeeded.</summary>
    public string? Error => Executed
        .Where(tool => tool.Result.IsError)
        .Select(tool => $"{tool.Invocation.Name}: {tool.Result.Content}")
        .FirstOrDefault();

    public RiskBriefState AddTo(RiskBriefState state) => state with
    {
        ToolCalls = [.. state.ToolCalls, .. Executed.Select(tool => tool.Invocation)],
        EngineError = state.EngineError ?? Error,
    };
}

/// <summary>
/// How graph nodes reach the engine: through the same tool catalog and executor the Copilot loop uses.
/// The graph, not a model, writes the arguments, but the approval gate, the telemetry and the record
/// of every call are the ones that already exist. Nothing here can reach the engine another way.
/// </summary>
internal sealed class BriefTools(ToolExecutor tools)
{
    /// <summary>Calls go to the executor this many at a time, so a cancelled request stops between groups instead of running every call.</summary>
    private const int CallsPerGroup = 8;

    public async Task<ToolBatch<T>> CallAsync<T>(string tool, IReadOnlyList<JsonElement> arguments, CancellationToken cancellationToken)
    {
        var calls = arguments.Select((input, index) => new ToolCallPart($"brief_{tool}_{index}", tool, input));
        var executed = new List<ExecutedTool>(arguments.Count);
        foreach (var group in calls.Chunk(CallsPerGroup))
        {
            cancellationToken.ThrowIfCancellationRequested();
            executed.AddRange(await tools.ExecuteAsync(group, cancellationToken).ConfigureAwait(false));
        }

        return new ToolBatch<T>(executed);
    }

    public static JsonElement Arguments(object arguments) => JsonSerializer.SerializeToElement(arguments);
}

/// <summary>The load_portfolio step.</summary>
internal sealed class LoadPortfolioNode(BriefTools tools) : IGraphNode<RiskBriefState>
{
    public async Task<RiskBriefState> RunAsync(RiskBriefState state, CancellationToken cancellationToken)
    {
        var batch = await tools
            .CallAsync<List<TradeSummary>>(ToolCatalog.ListTrades, [BriefTools.Arguments(new { })], cancellationToken)
            .ConfigureAwait(false);
        return batch.AddTo(state) with { Trades = batch.Single ?? [] };
    }
}

/// <summary>
/// One branch of the fan-out: the same tool once per argument set. The three branches differ only in
/// which tool, which arguments and where the results go.
/// </summary>
/// <param name="arguments">The argument sets to call the tool with, from the state.</param>
/// <param name="store">Puts the results into the state.</param>
internal sealed class ToolFanOutNode<T>(
    BriefTools tools,
    string tool,
    Func<RiskBriefState, IEnumerable<object>> arguments,
    Func<RiskBriefState, IReadOnlyList<T>, RiskBriefState> store) : IGraphNode<RiskBriefState>
{
    public async Task<RiskBriefState> RunAsync(RiskBriefState state, CancellationToken cancellationToken)
    {
        var batch = await tools
            .CallAsync<T>(tool, [.. arguments(state).Select(BriefTools.Arguments)], cancellationToken)
            .ConfigureAwait(false);
        return store(batch.AddTo(state), batch.Results);
    }
}

/// <summary>
/// The save_scenario step, reached only by resuming from the approval pause. The call still goes
/// through the approval gate: a resume without a person's yes is declined there, not here.
/// </summary>
internal sealed class SaveScenarioNode(BriefTools tools) : IGraphNode<RiskBriefState>
{
    public async Task<RiskBriefState> RunAsync(RiskBriefState state, CancellationToken cancellationToken)
    {
        if (state.Proposal is not { } proposal)
        {
            return state;
        }

        var batch = await tools
            .CallAsync<SavedScenario>(ProposedWrite.Tool, [proposal.ToArguments()], cancellationToken)
            .ConfigureAwait(false);

        // Declined and failed are different answers: one is the person's decision, the other is not.
        var failed = batch.Executed[0].Invocation.Outcome == ToolOutcome.Failed;
        return state with
        {
            ToolCalls = [.. state.ToolCalls, .. batch.Executed.Select(tool => tool.Invocation)],
            SavedScenario = batch.Single,
            SaveError = failed ? batch.Error : null,
        };
    }
}
