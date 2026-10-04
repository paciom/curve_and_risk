using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CurveRisk.Ai.Tools;

/// <param name="Function">Name, description, JSON schema and invoker, all derived from one C# method.</param>
/// <param name="RequiresApproval">True for tools that change stored state; hosts must get a human yes first.</param>
public sealed record CopilotTool(AIFunction Function, bool RequiresApproval)
{
    public string Name => Function.Name;
}

/// <summary>
/// Single source of truth for the AI tool surface. The MCP server and the in-process Copilot both
/// build from this catalog, so a tool cannot exist on one surface with a different schema on the other.
/// </summary>
public static class ToolCatalog
{
    public const string ListTrades = "list_trades";
    public const string GetCurve = "get_curve";
    public const string PriceTrade = "price_trade";
    public const string RunRisk = "run_risk";
    public const string RunScenario = "run_scenario";
    public const string SaveScenario = "save_scenario";

    public static IReadOnlyList<CopilotTool> Create(IRiskEngine engine)
    {
        var tools = new RiskTools(engine);
        return
        [
            Read(tools.ListTrades, ListTrades),
            Read(tools.GetCurve, GetCurve),
            Read(tools.PriceTrade, PriceTrade),
            Read(tools.RunRisk, RunRisk),
            Read(tools.RunScenario, RunScenario),
            new(Build(tools.SaveScenario, SaveScenario), RequiresApproval: true),
        ];
    }

    /// <summary>Invoke a tool with model-supplied JSON arguments and return its result as JSON text.</summary>
    public static async Task<string> InvokeAsync(CopilotTool tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        var args = new AIFunctionArguments();
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in arguments.EnumerateObject())
            {
                args[property.Name] = property.Value;
            }
        }

        var result = await tool.Function.InvokeAsync(args, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            null => "null",
            JsonElement element => element.GetRawText(),
            _ => JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions),
        };
    }

    private static CopilotTool Read(Delegate method, string name) => new(Build(method, name), RequiresApproval: false);

    private static AIFunction Build(Delegate method, string name) =>
        AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name });
}
