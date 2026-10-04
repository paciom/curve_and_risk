using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Engine;

namespace CurveRisk.Ai.Tests;

/// <summary>How a tool's return value becomes the JSON text sent back to the model.</summary>
public class ToolCatalogInvokeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly JsonElement NoArguments = JsonSerializer.SerializeToElement(new { });

    private static CopilotTool Returning(object? result) => new(new StubFunction("stub_tool", () => result), RequiresApproval: false);

    [Fact]
    public async Task A_tool_that_returns_nothing_yields_json_null()
    {
        var json = await ToolCatalog.InvokeAsync(Returning(null), NoArguments, Ct);

        Assert.Equal("null", json);
    }

    [Fact]
    public async Task A_json_result_is_returned_as_written()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{ "presentValue": 1672655.53 }""");

        var json = await ToolCatalog.InvokeAsync(Returning(element), NoArguments, Ct);

        Assert.Equal("""{ "presentValue": 1672655.53 }""", json);
    }

    [Fact]
    public async Task Any_other_result_is_serialised_to_json()
    {
        var json = await ToolCatalog.InvokeAsync(Returning(new List<double> { 1.5, 2.5 }), NoArguments, Ct);

        using var result = JsonDocument.Parse(json);
        Assert.Equal([1.5, 2.5], result.RootElement.EnumerateArray().Select(item => item.GetDouble()));
    }

    [Fact]
    public async Task Arguments_that_are_not_a_json_object_are_ignored()
    {
        var listTrades = ToolCatalog.Create(AnalyticsRiskEngine.CreateDemo()).Single(t => t.Name == ToolCatalog.ListTrades);
        var arguments = JsonSerializer.Deserialize<JsonElement>("""["T-1001"]""");

        using var result = JsonDocument.Parse(await ToolCatalog.InvokeAsync(listTrades, arguments, Ct));

        Assert.Equal("T-1001", result.RootElement[0].GetProperty("tradeId").GetString());
    }
}
