namespace CurveRisk.Copilot;

/// <summary>
/// The agent's only dependency on a model provider. Production uses <see cref="AnthropicModelClient"/>;
/// tests and grader self-checks use a scripted implementation, so the loop, the approval gate and the
/// grounding check are all testable without network access or spend.
/// </summary>
public interface IModelClient
{
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

/// <summary>The provider could not serve the request. <see cref="IsTransient"/> says whether a later retry may work.</summary>
public sealed class ModelClientException(string message, bool isTransient, Exception inner) : Exception(message, inner)
{
    public bool IsTransient { get; } = isTransient;
}
