using System.Globalization;
using CurveRisk.Ai.Tools;
using CurveRisk.Contracts.V1;
using CurveRisk.Copilot;
using CurveRisk.Engine;

namespace CurveRisk.Api.Services;

/// <summary>
/// How the Copilot is configured on this deployment: which provider (none, when no key is set), its
/// options, and the shared daily spend cap.
/// </summary>
/// <param name="Unavailable">Why there is no provider, for the 503 response. Empty when there is one.</param>
public sealed record CopilotConfiguration(ProviderSettings? Provider, CopilotOptions Options, BudgetGuard Budget, string Unavailable)
{
    private const decimal DefaultDailyBudgetUsd = 5m;

    public static CopilotConfiguration From(IConfiguration configuration)
    {
        var dailyBudget = decimal.TryParse(configuration["Copilot:DailyBudgetUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : DefaultDailyBudgetUsd;

        var provider = ProviderSettings.FromVariables(name => configuration[name], out var problem);
        return new CopilotConfiguration(provider, provider?.Options ?? new CopilotOptions(), new BudgetGuard(dailyBudget), problem);
    }
}

/// <summary>
/// Answers a question with the Risk Copilot over the stored book and a stored market snapshot.
/// An HTTP request has nobody to ask for approval, so write tools are always declined here.
/// </summary>
/// <param name="model">Absent when no provider is configured; the endpoint then answers 503.</param>
public sealed class CopilotService(
    MarketSnapshotService snapshots,
    TradeService trades,
    CopilotConfiguration configuration,
    IModelClient? model = null)
{
    private const int MaxQuestionLength = 2000;
    private const int MaxTradesInBook = 200;

    public async Task<CopilotAnswerResponse> AskAsync(CopilotQuestionRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        if (model is null)
        {
            throw new ServiceUnavailableException($"The Copilot is not configured on this deployment. {configuration.Unavailable}");
        }

        var market = await snapshots.GetMarketAsync(request.SnapshotId, cancellationToken).ConfigureAwait(false);
        var book = await trades.GetBookAsync(MaxTradesInBook, cancellationToken).ConfigureAwait(false);

        var answer = await Agent(model, market, book).AskAsync(request.Question, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ToResponse(answer);
    }

    private static void Validate(CopilotQuestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > MaxQuestionLength)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["question"] = [$"A question of up to {MaxQuestionLength} characters is required."],
            });
        }
    }

    private static CopilotAnswerResponse ToResponse(CopilotAnswer answer) => new(
        answer.Status.ToString(),
        answer.Text,
        [.. answer.ToolCalls.Select(call => new CopilotToolCallDto(call.Name, call.Outcome.ToString()))],
        answer.Grounding.Ungrounded,
        answer.ModelCalls,
        answer.Usage.InputTokens,
        answer.Usage.OutputTokens,
        answer.CostUsd);

    private CopilotAgent Agent(IModelClient client, MarketSnapshot market, IReadOnlyList<TradeDefinition> book)
    {
        var tools = new ToolExecutor(ToolCatalog.Create(new AnalyticsRiskEngine(market, book)), new DenyAllApprovalGate());
        return new CopilotAgent(client, tools, configuration.Options, configuration.Budget);
    }
}
