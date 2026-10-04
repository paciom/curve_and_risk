using CurveRisk.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CurveRisk.Api.Tests;

/// <summary>
/// The schema the application asks the database for. Column limits, the concurrency token and table
/// names are promises to the database and to migrations; a request-level test does not notice them
/// going missing on SQLite, which does not enforce lengths.
/// </summary>
public sealed class PersistenceModelTests : IDisposable
{
    // Provider caching is off so the model is built by this test, not reused from an earlier context.
    private readonly CurveRiskDbContext _db = new(new DbContextOptionsBuilder<CurveRiskDbContext>()
        .UseSqlite("Data Source=:memory:")
        .EnableServiceProviderCaching(false)
        .Options);

    public void Dispose() => _db.Dispose();

    private IEntityType Entity(Type type) => _db.Model.FindEntityType(type)!;

    [Theory]
    [InlineData(typeof(MarketSnapshotEntity), "market_snapshots", "Id")]
    [InlineData(typeof(TradeEntity), "trades", "TradeId")]
    [InlineData(typeof(RiskRunEntity), "risk_runs", "Id")]
    public void Each_entity_has_its_own_table_and_key(Type entity, string table, string key)
    {
        var mapped = Entity(entity);

        Assert.Equal(table, mapped.GetTableName());
        Assert.Equal([key], mapped.FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Theory]
    [InlineData(typeof(MarketSnapshotEntity), nameof(MarketSnapshotEntity.CurveId), 64)]
    [InlineData(typeof(MarketSnapshotEntity), nameof(MarketSnapshotEntity.AsOf), 10)]
    [InlineData(typeof(MarketSnapshotEntity), nameof(MarketSnapshotEntity.Interpolation), 64)]
    [InlineData(typeof(TradeEntity), nameof(TradeEntity.TradeId), 64)]
    [InlineData(typeof(TradeEntity), nameof(TradeEntity.Tenor), 8)]
    [InlineData(typeof(TradeEntity), nameof(TradeEntity.Description), 500)]
    [InlineData(typeof(TradeEntity), nameof(TradeEntity.IdempotencyKey), 128)]
    [InlineData(typeof(RiskRunEntity), nameof(RiskRunEntity.Status), 16)]
    public void Short_text_columns_are_bounded(Type entity, string property, int maxLength)
    {
        Assert.Equal(maxLength, Entity(entity).FindProperty(property)!.GetMaxLength());
    }

    [Fact]
    public void A_notional_is_stored_exactly_to_four_decimal_places()
    {
        var notional = Entity(typeof(TradeEntity)).FindProperty(nameof(TradeEntity.NotionalAmount))!;

        Assert.Equal((28, 4), (notional.GetPrecision(), notional.GetScale()));
    }

    [Fact]
    public void The_trade_version_guards_every_write_against_a_concurrent_change()
    {
        Assert.True(Entity(typeof(TradeEntity)).FindProperty(nameof(TradeEntity.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void Snapshots_are_indexed_by_curve_and_date()
    {
        var index = Assert.Single(Entity(typeof(MarketSnapshotEntity)).GetIndexes());

        Assert.Equal(
            [nameof(MarketSnapshotEntity.CurveId), nameof(MarketSnapshotEntity.AsOf)],
            index.Properties.Select(property => property.Name));
    }
}
