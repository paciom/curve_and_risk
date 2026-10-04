using CurveRisk.Ai.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol;

namespace CurveRisk.Mcp;

/// <summary>
/// The MCP SDK hides exception messages from clients by default, which is right for unexpected failures
/// but leaves a model unable to correct a bad argument. Engine validation errors are safe to show, so
/// they are rethrown as <see cref="McpException"/>, whose message the SDK does pass through.
/// </summary>
internal sealed class EngineErrorSurfacingFunction(AIFunction inner) : DelegatingAIFunction(inner)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (RiskEngineException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
