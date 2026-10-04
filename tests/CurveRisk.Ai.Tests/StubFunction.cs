using Microsoft.Extensions.AI;

namespace CurveRisk.Ai.Tests;

/// <summary>A tool whose result, or failure, is fixed by the test.</summary>
internal sealed class StubFunction(string name, Func<object?> invoke) : AIFunction
{
    public override string Name => name;

    public int Invocations { get; private set; }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        Invocations++;
        return new ValueTask<object?>(invoke());
    }
}
