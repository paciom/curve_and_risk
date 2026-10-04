using CurveRisk.Ai.Tools;
using CurveRisk.Ai.Tools.Fixtures;
using CurveRisk.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// The stdio transport owns stdout for JSON-RPC, so every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Placeholder engine until CurveRisk.Analytics exists; see FixtureRiskEngine.
IRiskEngine engine = new FixtureRiskEngine();

// An MCP server cannot ask a person for approval; whether to prompt is up to each client. So tools that
// change state are not offered at all unless the operator starts the server with --allow-writes.
var allowWrites = args.Contains("--allow-writes", StringComparer.OrdinalIgnoreCase);

var tools = ToolCatalog.Create(engine)
    .Where(tool => allowWrites || !tool.RequiresApproval)
    .Select(tool => McpServerTool.Create(
    new EngineErrorSurfacingFunction(tool.Function),
    new McpServerToolCreateOptions
    {
        // Hints only: MCP clients decide whether to prompt.
        ReadOnly = !tool.RequiresApproval,
        Destructive = false,
        Idempotent = !tool.RequiresApproval,
        OpenWorld = false,
    }));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools(tools);

await builder.Build().RunAsync();
