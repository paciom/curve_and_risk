using Microsoft.EntityFrameworkCore;

namespace CurveRisk.Api.Persistence;

public sealed class MarketSnapshotEntity
{
    public Guid Id { get; set; }

    public required string CurveId { get; set; }

    /// <summary>yyyy-MM-dd.</summary>
    public required string AsOf { get; set; }

    public required string Interpolation { get; set; }

    /// <summary>The quotes as a JSON array of tenor and rate in percent.</summary>
    public required string QuotesJson { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class TradeEntity
{
    public required string TradeId { get; set; }

    public decimal NotionalAmount { get; set; }

    public double FixedRatePercent { get; set; }

    public bool PayFixed { get; set; }

    public required string Tenor { get; set; }

    public required string Description { get; set; }

    /// <summary>Incremented on every update; the ETag clients must send back to change or delete the trade.</summary>
    public int Version { get; set; }

    /// <summary>The key the creating request carried, so a retry of the same request is recognised.</summary>
    public string? IdempotencyKey { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class RiskRunEntity
{
    public Guid Id { get; set; }

    public Guid SnapshotId { get; set; }

    public required string TradeIdsJson { get; set; }

    public required string Status { get; set; }

    public string? ResultsJson { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class CurveRiskDbContext(DbContextOptions<CurveRiskDbContext> options) : DbContext(options)
{
    public DbSet<MarketSnapshotEntity> MarketSnapshots => Set<MarketSnapshotEntity>();

    public DbSet<TradeEntity> Trades => Set<TradeEntity>();

    public DbSet<RiskRunEntity> RiskRuns => Set<RiskRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketSnapshotEntity>(entity =>
        {
            entity.ToTable("market_snapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.CurveId).HasMaxLength(64);
            entity.Property(snapshot => snapshot.AsOf).HasMaxLength(10);
            entity.Property(snapshot => snapshot.Interpolation).HasMaxLength(64);
            entity.HasIndex(snapshot => new { snapshot.CurveId, snapshot.AsOf });
        });

        modelBuilder.Entity<TradeEntity>(entity =>
        {
            entity.ToTable("trades");
            entity.HasKey(trade => trade.TradeId);
            entity.Property(trade => trade.TradeId).HasMaxLength(64);
            entity.Property(trade => trade.NotionalAmount).HasPrecision(28, 4);
            entity.Property(trade => trade.Tenor).HasMaxLength(8);
            entity.Property(trade => trade.Description).HasMaxLength(500);
            entity.Property(trade => trade.IdempotencyKey).HasMaxLength(128);
            entity.Property(trade => trade.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<RiskRunEntity>(entity =>
        {
            entity.ToTable("risk_runs");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Status).HasMaxLength(16);
        });
    }
}
