using Anthropic;
using Anthropic.Exceptions;
using Sdk = Anthropic.Models.Messages;

namespace CurveRisk.Copilot;

/// <summary>
/// Adapter between the agent's provider-neutral conversation model and the Anthropic Messages API.
/// Together with <see cref="AnthropicRequestMapper"/> and <see cref="AnthropicResponseMapper"/>, the
/// only code that knows about the SDK.
/// </summary>
public sealed class AnthropicModelClient(AnthropicClient client, CopilotOptions options) : IModelClient
{
    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var parameters = AnthropicRequestMapper.ToParameters(request, options);
        var message = await SendAsync(parameters, cancellationToken).ConfigureAwait(false);
        return AnthropicResponseMapper.ToResponse(message);
    }

    /// <summary>Translates SDK failures into the port's one exception, most specific first, keeping "may a retry help?".</summary>
    private async Task<Sdk.Message> SendAsync(Sdk.MessageCreateParams parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await client.Messages.Create(parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new ModelClientException("Rate limited by the model provider.", isTransient: true, ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new ModelClientException("The model provider returned a server error.", isTransient: true, ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new ModelClientException("Could not reach the model provider.", isTransient: true, ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new ModelClientException("The model provider rejected the request.", isTransient: false, ex);
        }
    }
}
