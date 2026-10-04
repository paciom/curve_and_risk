using System.Net.Http.Json;
using CurveRisk.Api.Persistence;
using CurveRisk.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CurveRisk.Api.Tests;

/// <summary>Behaviour that cannot be reached through an HTTP request alone: the worker's failure path and start-up wiring.</summary>
public sealed class ApiInternalsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_whose_trade_disappears_before_it_is_computed_is_recorded_as_failed_not_lost()
    {
        var client = factory.CreateClient();
        var snapshotId = (await ApiFactory.CreateSnapshotAsync(client, Ct)).Id;
        (await client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade("R-3"), Ct)).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CurveRiskDbContext>();
        var run = new RiskRunEntity
        {
            Id = Guid.NewGuid(),
            SnapshotId = snapshotId,
            TradeIdsJson = "[\"R-3\",\"R-VANISHED\"]",
            Status = RiskRunStatus.Pending,
        };
        db.RiskRuns.Add(run);
        await db.SaveChangesAsync(Ct);
        var service = scope.ServiceProvider.GetRequiredService<RiskRunService>();

        await service.ProcessAsync(run.Id, Ct);
        await service.ProcessAsync(run.Id, Ct);
        await service.ProcessAsync(Guid.NewGuid(), Ct);

        var stored = await service.GetAsync(run.Id, Ct);
        Assert.Equal(RiskRunStatus.Failed, stored.Status);
        Assert.Contains("R-VANISHED", stored.Error, StringComparison.Ordinal);
        Assert.Null(stored.Results);
    }

    [Fact]
    public async Task An_unexpected_exception_is_left_to_the_framework_and_not_described_to_the_caller()
    {
        using var scope = factory.Services.CreateScope();
        var handler = new ProblemDetailsExceptionHandler(scope.ServiceProvider.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(), new InvalidCastException("internal detail"), Ct);

        Assert.False(handled);
    }

    [Fact]
    public void The_database_provider_is_chosen_by_configuration_and_a_wrong_one_is_refused()
    {
        static DbContextOptions Configure(params (string Key, string Value)[] settings)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build();
            var builder = new DbContextOptionsBuilder<CurveRiskDbContext>();
            ApiSetup.ConfigureDatabase(builder, configuration);
            return builder.Options;
        }

        static string Provider(DbContextOptions options) => string.Join(",", options.Extensions.Select(extension => extension.GetType().Name));

        Assert.Contains("Sqlite", Provider(Configure()), StringComparison.Ordinal);
        Assert.Contains("Npgsql", Provider(Configure(("Database:Provider", "postgres"), ("ConnectionStrings:CurveRisk", "Host=db"))), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => Configure(("Database:Provider", "Postgres")));
        Assert.Throws<InvalidOperationException>(() => Configure(("Database:Provider", "Oracle")));
    }
}
