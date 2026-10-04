using System.Text.Json;
using System.Threading.Channels;
using CurveRisk.Api.Persistence;
using CurveRisk.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace CurveRisk.Api.Services;

public static class RiskRunStatus
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

/// <summary>The queue between the request that accepts a risk run and the worker that computes it.</summary>
public sealed class RiskRunBacklog
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(runId, cancellationToken);
}

/// <summary>
/// Risk runs are a job resource: creating one records it as Pending and returns at once; the
/// calculation happens off the request thread and the caller polls for the result.
/// </summary>
public sealed class RiskRunService(CurveRiskDbContext db, PricingService pricing, RiskRunBacklog queue, TimeProvider clock)
{
    public async Task<RiskRunResponse> CreateAsync(CreateRiskRunRequest request, CancellationToken cancellationToken)
    {
        if (request.TradeIds is null || request.TradeIds.Count == 0)
        {
            throw new RequestValidationException(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["tradeIds"] = ["At least one trade id is required."] });
        }

        // Fail now, with a 404, for a snapshot or trade that does not exist, not later inside the job.
        await pricing.CurveAsync(request.SnapshotId, cancellationToken).ConfigureAwait(false);
        await pricing.ValueAsync(new ValuationRequest(request.SnapshotId, request.TradeIds[0]), cancellationToken).ConfigureAwait(false);

        var entity = new RiskRunEntity
        {
            Id = Guid.CreateVersion7(clock.GetUtcNow()),
            SnapshotId = request.SnapshotId,
            TradeIdsJson = JsonSerializer.Serialize(request.TradeIds),
            Status = RiskRunStatus.Pending,
            CreatedUtc = clock.GetUtcNow(),
        };
        db.RiskRuns.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await queue.EnqueueAsync(entity.Id, cancellationToken).ConfigureAwait(false);
        return ToResponse(entity);
    }

    public async Task<RiskRunResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await db.RiskRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Risk run", id.ToString()));

    /// <summary>Computes a pending run and records the outcome. A failure is stored on the run, not thrown.</summary>
    public async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.RiskRuns.FirstOrDefaultAsync(run => run.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null || entity.Status != RiskRunStatus.Pending)
        {
            return;
        }

        try
        {
            var results = await pricing.RiskAsync(entity.SnapshotId, TradeIds(entity), cancellationToken).ConfigureAwait(false);
            entity.ResultsJson = JsonSerializer.Serialize(results);
            entity.Status = RiskRunStatus.Completed;
        }
        catch (Exception ex) when (ex is ApiException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            entity.Error = ex.Message;
            entity.Status = RiskRunStatus.Failed;
        }

        entity.CompletedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<string> TradeIds(RiskRunEntity entity) => JsonSerializer.Deserialize<List<string>>(entity.TradeIdsJson) ?? [];

    private static RiskRunResponse ToResponse(RiskRunEntity entity) => new(
        entity.Id,
        entity.Status,
        entity.SnapshotId,
        TradeIds(entity),
        entity.ResultsJson is null ? null : JsonSerializer.Deserialize<List<TradeRiskDto>>(entity.ResultsJson),
        entity.Error,
        entity.CreatedUtc,
        entity.CompletedUtc);
}

/// <summary>Drains the queue for the life of the application, one run at a time, each in its own scope.</summary>
public sealed class RiskRunWorker(RiskRunBacklog queue, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await scope.ServiceProvider.GetRequiredService<RiskRunService>().ProcessAsync(runId, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
