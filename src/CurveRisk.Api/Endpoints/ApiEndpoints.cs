using CurveRisk.Api.Services;
using CurveRisk.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace CurveRisk.Api.Endpoints;

/// <summary>
/// Version 1 routes. Handlers bind, call one service method and shape the HTTP response; they hold no
/// logic of their own. A breaking change gets a new group under /api/v2 and leaves these as they are.
/// </summary>
public static class ApiEndpoints
{
    private const int DefaultPageSize = 50;

    public static IEndpointRouteBuilder MapCurveRiskV1(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1");
        MapMarketSnapshots(v1.MapGroup("/market-snapshots").WithTags("Market snapshots"));
        MapTrades(v1.MapGroup("/trades").WithTags("Trades"));
        MapCalculations(v1.WithTags("Calculations"));
        return app;
    }

    private static void MapMarketSnapshots(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateMarketSnapshotRequest request, MarketSnapshotService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/market-snapshots/{created.Id}", created);
        }).ProducesValidationProblem().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (Guid? cursor, int? pageSize, MarketSnapshotService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(cursor, pageSize ?? DefaultPageSize, ct).ConfigureAwait(false)));

        group.MapGet("/{id:guid}", async (Guid id, MarketSnapshotService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct).ConfigureAwait(false)))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/curve", async (Guid id, PricingService pricing, CancellationToken ct) =>
            TypedResults.Ok(await pricing.CurveAsync(id, ct).ConfigureAwait(false)))
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapTrades(RouteGroupBuilder group)
    {
        group.MapPost("/", CreateTradeAsync)
            .Produces<TradeResponse>(StatusCodes.Status201Created)
            .Produces<TradeResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (string? cursor, int? pageSize, TradeService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(cursor, pageSize ?? DefaultPageSize, ct).ConfigureAwait(false)));

        group.MapGet("/{tradeId}", async (string tradeId, TradeService service, HttpContext http, CancellationToken ct) =>
        {
            var stored = await service.GetAsync(tradeId, ct).ConfigureAwait(false);
            http.Response.Headers.ETag = stored.ETag;
            return TypedResults.Ok(stored.Trade);
        }).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{tradeId}", UpdateTradeAsync)
            .Produces<TradeResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapDelete("/{tradeId}", async (string tradeId, [FromHeader(Name = "If-Match")] string? ifMatch, TradeService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(tradeId, ifMatch, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status412PreconditionFailed)
          .ProducesProblem(StatusCodes.Status428PreconditionRequired);
    }

    private static void MapCalculations(RouteGroupBuilder group)
    {
        group.MapPost("/valuations", async (ValuationRequest request, PricingService pricing, CancellationToken ct) =>
            TypedResults.Ok(await pricing.ValueAsync(request, ct).ConfigureAwait(false)))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/scenario-runs", async (ScenarioRunRequest request, PricingService pricing, CancellationToken ct) =>
            TypedResults.Ok(await pricing.RunScenarioAsync(request, ct).ConfigureAwait(false)))
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/risk-runs", async (CreateRiskRunRequest request, RiskRunService service, CancellationToken ct) =>
        {
            var run = await service.CreateAsync(request, ct).ConfigureAwait(false);
            return TypedResults.Accepted($"/api/v1/risk-runs/{run.Id}", run);
        }).ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/copilot/answers", async (CopilotQuestionRequest request, CopilotService copilot, CancellationToken ct) =>
            TypedResults.Ok(await copilot.AskAsync(request, ct).ConfigureAwait(false)))
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/risk-runs/{id:guid}", async (Guid id, RiskRunService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct).ConfigureAwait(false)))
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateTradeAsync(
        CreateTradeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        TradeService service,
        HttpContext http)
    {
        var result = await service.CreateAsync(request, idempotencyKey, http.RequestAborted).ConfigureAwait(false);
        http.Response.Headers.ETag = result.Stored.ETag;

        // A recognised retry returns the original with 200: the trade exists, and nothing new was made.
        return result.Created
            ? TypedResults.Created($"/api/v1/trades/{result.Stored.Trade.TradeId}", result.Stored.Trade)
            : TypedResults.Ok(result.Stored.Trade);
    }

    private static async Task<IResult> UpdateTradeAsync(
        string tradeId,
        UpdateTradeRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        TradeService service,
        HttpContext http)
    {
        var stored = await service.UpdateAsync(tradeId, request, ifMatch, http.RequestAborted).ConfigureAwait(false);
        http.Response.Headers.ETag = stored.ETag;
        return TypedResults.Ok(stored.Trade);
    }
}
