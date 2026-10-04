using CurveRisk.Ai.Tools;
using CurveRisk.Contracts.V1;
using CurveRisk.Engine;

namespace CurveRisk.Api.Services;

/// <summary>
/// Prices stored trades against a stored market snapshot. The same engine that serves the AI tools
/// does the work, so the API and the Copilot cannot disagree about a number.
/// </summary>
public sealed class PricingService(MarketSnapshotService snapshots, TradeService trades)
{
    public async Task<CurveResponse> CurveAsync(Guid snapshotId, CancellationToken cancellationToken)
    {
        var market = await snapshots.GetMarketAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        var curve = await new AnalyticsRiskEngine(market, []).GetCurveAsync(market.CurveId, cancellationToken).ConfigureAwait(false);

        return new CurveResponse(
            snapshotId,
            curve.CurveId,
            curve.AsOf,
            curve.Interpolation,
            [.. curve.Pillars.Select(pillar => new CurvePillarDto(pillar.TenorYears, pillar.ZeroRatePercent, pillar.DiscountFactor))]);
    }

    public async Task<ValuationResponse> ValueAsync(ValuationRequest request, CancellationToken cancellationToken)
    {
        var engine = await EngineAsync(request.SnapshotId, [request.TradeId], cancellationToken).ConfigureAwait(false);
        var valuation = await engine.PriceTradeAsync(request.TradeId, cancellationToken).ConfigureAwait(false);

        return new ValuationResponse(
            valuation.TradeId, valuation.Currency, valuation.PresentValue, valuation.ParRatePercent, valuation.FixedRatePercent);
    }

    public async Task<ScenarioRunResponse> RunScenarioAsync(ScenarioRunRequest request, CancellationToken cancellationToken)
    {
        var engine = await EngineAsync(request.SnapshotId, [request.TradeId], cancellationToken).ConfigureAwait(false);
        var shock = new ScenarioShock(request.ParallelBp, request.SteepenerBp);
        var result = await engine.RunScenarioAsync(request.TradeId, shock, cancellationToken).ConfigureAwait(false);

        return new ScenarioRunResponse(
            result.TradeId,
            result.Currency,
            request.ParallelBp,
            request.SteepenerBp,
            result.BasePresentValue,
            result.ShockedPresentValue,
            result.ProfitAndLoss);
    }

    public async Task<IReadOnlyList<TradeRiskDto>> RiskAsync(Guid snapshotId, IReadOnlyList<string> tradeIds, CancellationToken cancellationToken)
    {
        var engine = await EngineAsync(snapshotId, tradeIds, cancellationToken).ConfigureAwait(false);
        var results = new List<TradeRiskDto>(tradeIds.Count);
        foreach (var tradeId in tradeIds)
        {
            var risk = await engine.RunRiskAsync(tradeId, cancellationToken).ConfigureAwait(false);
            results.Add(new TradeRiskDto(
                risk.TradeId,
                risk.Currency,
                risk.ParallelDv01,
                [.. risk.Buckets.Select(bucket => new BucketDeltaDto(bucket.TenorYears, bucket.Delta))]));
        }

        return results;
    }

    private async Task<AnalyticsRiskEngine> EngineAsync(Guid snapshotId, IReadOnlyList<string> tradeIds, CancellationToken cancellationToken)
    {
        var market = await snapshots.GetMarketAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        var definitions = await trades.GetDefinitionsAsync([.. tradeIds.Distinct(StringComparer.Ordinal)], cancellationToken).ConfigureAwait(false);
        return new AnalyticsRiskEngine(market, definitions);
    }
}
