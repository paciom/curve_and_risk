using Anthropic;

namespace CurveRisk.Copilot;

/// <summary>
/// A model client for a configured provider that owns its SDK client and releases it on dispose.
/// Hosts use this type, so they never reference the SDK themselves.
/// </summary>
public sealed class AnthropicProviderClient : IModelClient, IDisposable
{
    private readonly AnthropicClient _sdk;
    private readonly AnthropicModelClient _adapter;

    public AnthropicProviderClient(ProviderSettings settings, CopilotOptions? options = null)
    {
        _sdk = NewSdkClient(settings);
        _adapter = new AnthropicModelClient(_sdk, options ?? settings.Options);
    }

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
        _adapter.CompleteAsync(request, cancellationToken);

    public void Dispose() => _sdk.Dispose();

    private static AnthropicClient NewSdkClient(ProviderSettings settings) => settings.BaseUrl is null
        ? new AnthropicClient { ApiKey = settings.ApiKey }
        : new AnthropicClient { ApiKey = settings.ApiKey, BaseUrl = settings.BaseUrl.ToString() };
}
