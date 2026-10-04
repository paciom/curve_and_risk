using System.Globalization;
using CurveRisk.Analytics.Instruments;
using CurveRisk.Analytics.Time;
using CurveRisk.Api.Persistence;
using CurveRisk.Contracts.V1;
using CurveRisk.Engine;
using Microsoft.EntityFrameworkCore;

namespace CurveRisk.Api.Services;

/// <summary>A trade as returned to a caller, with the version tag needed to change it.</summary>
public sealed record StoredTrade(TradeResponse Trade, string ETag);

/// <summary>What happened when a trade was posted: created now, or recognised as a retry of an earlier request.</summary>
public sealed record CreateTradeResult(StoredTrade Stored, bool Created);

/// <summary>Books and maintains trades, with idempotent creation and optimistic concurrency on change.</summary>
public sealed class TradeService(CurveRiskDbContext db, TimeProvider clock)
{
    private const string PayFixed = "PayFixed";
    private const string ReceiveFixed = "ReceiveFixed";
    private const double PercentToFraction = 0.01;
    private const int MaxPageSize = 100;

    /// <summary>
    /// Creates the trade. Posting the same id again with the same Idempotency-Key returns the original
    /// (a safe retry); with a different or missing key it is a conflict.
    /// </summary>
    public async Task<CreateTradeResult> CreateAsync(CreateTradeRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        Validate(request.TradeId, request.Direction, new UpdateTradeRequest(request.NotionalAmount, request.FixedRatePercent, request.Tenor, request.Description));

        var existing = await db.Trades.AsNoTracking()
            .FirstOrDefaultAsync(trade => trade.TradeId == request.TradeId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return idempotencyKey is not null && existing.IdempotencyKey == idempotencyKey
                ? new CreateTradeResult(ToStored(existing), Created: false)
                : throw new ConflictException($"Trade '{request.TradeId}' already exists.");
        }

        var entity = new TradeEntity
        {
            TradeId = request.TradeId.Trim(),
            NotionalAmount = request.NotionalAmount,
            FixedRatePercent = request.FixedRatePercent,
            PayFixed = request.Direction == PayFixed,
            Tenor = Tenor.Parse(request.Tenor).ToString(),
            Description = request.Description ?? string.Empty,
            Version = 1,
            IdempotencyKey = idempotencyKey,
            CreatedUtc = clock.UtcNowToMillisecond(),
        };
        db.Trades.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CreateTradeResult(ToStored(entity), Created: true);
    }

    public async Task<StoredTrade> GetAsync(string tradeId, CancellationToken cancellationToken) =>
        ToStored(await FindAsync(tradeId, track: false, cancellationToken).ConfigureAwait(false));

    public async Task<PageResponse<TradeResponse>> ListAsync(string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var after = cursor ?? string.Empty;
        var entities = await db.Trades.AsNoTracking()
            .Where(trade => string.Compare(trade.TradeId, after) > 0)
            .OrderBy(trade => trade.TradeId)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var page = entities.Take(size).Select(entity => ToStored(entity).Trade).ToList();
        return new PageResponse<TradeResponse>(page, entities.Count > size ? page[^1].TradeId : null);
    }

    public async Task<StoredTrade> UpdateAsync(string tradeId, UpdateTradeRequest request, string? ifMatch, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(tradeId, track: true, cancellationToken).ConfigureAwait(false);
        RequireMatch(entity, ifMatch);
        Validate(tradeId, entity.PayFixed ? PayFixed : ReceiveFixed, request);

        entity.NotionalAmount = request.NotionalAmount;
        entity.FixedRatePercent = request.FixedRatePercent;
        entity.Tenor = Tenor.Parse(request.Tenor).ToString();
        entity.Description = request.Description ?? string.Empty;
        entity.Version++;
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return ToStored(entity);
    }

    public async Task DeleteAsync(string tradeId, string? ifMatch, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(tradeId, track: true, cancellationToken).ConfigureAwait(false);
        RequireMatch(entity, ifMatch);
        db.Trades.Remove(entity);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The stored trades with these ids, as the engine's trade type. Unknown ids are a 404.</summary>
    public async Task<IReadOnlyList<TradeDefinition>> GetDefinitionsAsync(IReadOnlyCollection<string> tradeIds, CancellationToken cancellationToken)
    {
        var entities = await db.Trades.AsNoTracking()
            .Where(trade => tradeIds.Contains(trade.TradeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var missing = tradeIds.Except(entities.Select(entity => entity.TradeId), StringComparer.Ordinal).FirstOrDefault();
        return missing is null
            ? [.. entities.Select(ToDefinition)]
            : throw new NotFoundException("Trade", missing);
    }

    /// <summary>The whole book as the engine's trade type, up to <paramref name="limit"/> trades in id order.</summary>
    public async Task<IReadOnlyList<TradeDefinition>> GetBookAsync(int limit, CancellationToken cancellationToken)
    {
        var entities = await db.Trades.AsNoTracking()
            .OrderBy(trade => trade.TradeId)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. entities.Select(ToDefinition)];
    }

    private static TradeDefinition ToDefinition(TradeEntity entity) => new(
        entity.TradeId,
        new SwapTerms((double)entity.NotionalAmount, entity.FixedRatePercent * PercentToFraction, entity.PayFixed, Tenor.Parse(entity.Tenor)),
        entity.Description);

    private static StoredTrade ToStored(TradeEntity entity) => new(
        new TradeResponse(
            entity.TradeId,
            entity.NotionalAmount,
            entity.FixedRatePercent,
            entity.PayFixed ? PayFixed : ReceiveFixed,
            entity.Tenor,
            entity.Description,
            entity.CreatedUtc),
        ETagOf(entity.Version));

    private static string ETagOf(int version) => $"\"v{version.ToString(CultureInfo.InvariantCulture)}\"";

    private static void RequireMatch(TradeEntity entity, string? ifMatch)
    {
        if (string.IsNullOrEmpty(ifMatch))
        {
            throw new PreconditionRequiredException();
        }

        if (ifMatch != ETagOf(entity.Version))
        {
            throw new PreconditionFailedException();
        }
    }

    private static void Validate(string tradeId, string direction, UpdateTradeRequest terms)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(tradeId) || tradeId.Length > 64)
        {
            errors["tradeId"] = ["A trade id of up to 64 characters is required."];
        }

        if (direction is not (PayFixed or ReceiveFixed))
        {
            errors["direction"] = [$"Use {PayFixed} or {ReceiveFixed}."];
        }

        if (terms.NotionalAmount <= 0)
        {
            errors["notionalAmount"] = ["The notional must be positive."];
        }

        if (!double.IsFinite(terms.FixedRatePercent) || Math.Abs(terms.FixedRatePercent) > 100)
        {
            errors["fixedRatePercent"] = ["The fixed rate must be between -100 and 100 percent."];
        }

        AddTenorError(terms.Tenor, errors);
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }

    private static void AddTenorError(string tenor, Dictionary<string, string[]> errors)
    {
        try
        {
            Tenor.Parse(tenor);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException)
        {
            errors["tenor"] = ["Use a positive count and a unit, such as 6M or 10Y."];
        }
    }

    private async Task<TradeEntity> FindAsync(string tradeId, bool track, CancellationToken cancellationToken)
    {
        var query = track ? db.Trades : db.Trades.AsNoTracking();
        return await query.FirstOrDefaultAsync(trade => trade.TradeId == tradeId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Trade", tradeId);
    }

    /// <summary>A concurrent writer who got there first shows up as a concurrency exception: the same answer as a stale ETag.</summary>
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PreconditionFailedException();
        }
    }
}
