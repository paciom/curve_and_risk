using System.Net;
using CurveRisk.Api.Persistence;
using CurveRisk.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CurveRisk.Api.Tests;

/// <summary>How the application chooses and prepares its database when it starts.</summary>
public sealed class StartupTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static DbContextOptions<CurveRiskDbContext> Configure(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        var builder = new DbContextOptionsBuilder<CurveRiskDbContext>();
        ApiSetup.ConfigureDatabase(builder, configuration);
        return builder.Options;
    }

    [Fact]
    public void Without_a_connection_string_the_local_database_is_the_file_curverisk_db()
    {
        using var db = new CurveRiskDbContext(Configure());

        Assert.Equal("Data Source=curverisk.db", db.Database.GetConnectionString());
    }

    [Fact]
    public void A_configured_connection_string_is_the_one_used()
    {
        using var db = new CurveRiskDbContext(Configure(("ConnectionStrings:CurveRisk", "Data Source=elsewhere.db")));

        Assert.Equal("Data Source=elsewhere.db", db.Database.GetConnectionString());
    }

    [Fact]
    public void Postgres_without_a_connection_string_is_refused_with_the_setting_to_fix()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Configure(("Database:Provider", "Postgres")));

        Assert.Equal("ConnectionStrings:CurveRisk is required for the Postgres provider.", refused.Message);
    }

    [Fact]
    public void An_unknown_provider_is_refused_with_the_choices()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Configure(("Database:Provider", "Oracle")));

        Assert.Equal("Unknown Database:Provider 'Oracle'. Use Postgres or Sqlite.", refused.Message);
    }

    [Fact]
    public async Task A_postgres_deployment_starts_without_touching_the_schema_unless_told_to()
    {
        await using var app = new UnreachablePostgresFactory();

        var health = await app.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    /// <summary>
    /// A PostgreSQL deployment whose database cannot be reached. Starting it succeeds only if start-up
    /// leaves the database alone, which is the rule for a production schema.
    /// </summary>
    private sealed class UnreachablePostgresFactory : WebApplicationFactory<ProblemDetailsExceptionHandler>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Provider", ApiSetup.PostgresProvider);
            builder.UseSetting("ConnectionStrings:CurveRisk", "Host=127.0.0.1;Port=1;Timeout=1;Database=never_opened");
        }
    }
}
