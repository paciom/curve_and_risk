using System.Globalization;
using System.Text.Json;
using CurveRisk.Analytics.Curves;
using CurveRisk.Analytics.Time;
using CurveRisk.Api.Persistence;
using CurveRisk.Contracts.V1;
using CurveRisk.Engine;
using Microsoft.EntityFrameworkCore;

namespace CurveRisk.Api.Services;

/// <summary>
/// Stores market snapshots. A snapshot is immutable once created and is checked at creation: if a
/// curve cannot be calibrated from it, it is rejected then, not when someone later prices against it.
/// </summary>
public sealed class MarketSnapshotService(CurveRiskDbContext db, TimeProvider clock)
{
    private const string DateFormat = "yyyy-MM-dd";
    private const int MaxPageSize = 100;

    public async Task<MarketSnapshotResponse> CreateAsync(CreateMarketSnapshotRequest request, CancellationToken cancellationToken)
    {
        var market = Parse(request);

        // Throws CalibrationException (a 422) when the quotes do not make a curve.
        market.ToCurveMarket().Calibrate();

        var entity = new MarketSnapshotEntity
        {
            Id = Guid.CreateVersion7(clock.GetUtcNow()),
            CurveId = market.CurveId,
            AsOf = request.AsOf,
            Interpolation = market.Scheme.ToString(),
            QuotesJson = JsonSerializer.Serialize(request.Quotes),
            CreatedUtc = clock.GetUtcNow(),
        };
        db.MarketSnapshots.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(entity);
    }

    public async Task<MarketSnapshotResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await FindAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>The stored snapshot as the engine's market type.</summary>
    public async Task<MarketSnapshot> GetMarketAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        return Parse(new CreateMarketSnapshotRequest(entity.CurveId, entity.AsOf, entity.Interpolation, Quotes(entity)));
    }

    /// <summary>Newest last. Ids are time-ordered, so the id of the last item is the cursor.</summary>
    public async Task<PageResponse<MarketSnapshotResponse>> ListAsync(Guid? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var after = cursor ?? Guid.Empty;
        var entities = await db.MarketSnapshots
            .Where(snapshot => snapshot.Id > after)
            .OrderBy(snapshot => snapshot.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var page = entities.Take(size).Select(ToResponse).ToList();
        return new PageResponse<MarketSnapshotResponse>(page, entities.Count > size ? page[^1].Id.ToString() : null);
    }

    private static MarketSnapshot Parse(CreateMarketSnapshotRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.CurveId))
        {
            errors["curveId"] = ["A curve id is required."];
        }

        if (!DateTime.TryParseExact(request.AsOf, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
        {
            errors["asOf"] = ["Use the format yyyy-MM-dd."];
        }

        if (!Enum.TryParse<InterpolationScheme>(request.Interpolation, ignoreCase: false, out var scheme) || !Enum.IsDefined(scheme))
        {
            errors["interpolation"] = [$"Use one of: {string.Join(", ", Enum.GetNames<InterpolationScheme>())}."];
        }

        var quotes = ParseQuotes(request.Quotes, errors);
        return errors.Count == 0
            ? new MarketSnapshot(request.CurveId.Trim(), asOf, quotes, scheme)
            : throw new RequestValidationException(errors);
    }

    private static List<ParQuote> ParseQuotes(IReadOnlyList<QuoteDto>? quotes, Dictionary<string, string[]> errors)
    {
        var parsed = new List<ParQuote>();
        if (quotes is null || quotes.Count == 0)
        {
            errors["quotes"] = ["At least one quote is required."];
            return parsed;
        }

        foreach (var (quote, index) in quotes.Select((quote, index) => (quote, index)))
        {
            try
            {
                parsed.Add(new ParQuote(Tenor.Parse(quote.Tenor), quote.RatePercent));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentNullException)
            {
                errors[$"quotes[{index}].tenor"] = ["Use a positive count and a unit, such as 6M or 10Y."];
            }
        }

        return parsed;
    }

    private static List<QuoteDto> Quotes(MarketSnapshotEntity entity) =>
        JsonSerializer.Deserialize<List<QuoteDto>>(entity.QuotesJson) ?? [];

    private static MarketSnapshotResponse ToResponse(MarketSnapshotEntity entity) =>
        new(entity.Id, entity.CurveId, entity.AsOf, entity.Interpolation, Quotes(entity), entity.CreatedUtc);

    private async Task<MarketSnapshotEntity> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.MarketSnapshots.AsNoTracking().FirstOrDefaultAsync(snapshot => snapshot.Id == id, cancellationToken).ConfigureAwait(false)
        ?? throw new NotFoundException("Market snapshot", id.ToString());
}
