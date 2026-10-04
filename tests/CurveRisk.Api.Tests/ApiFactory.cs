using System.Net.Http.Json;
using CurveRisk.Api.Services;
using CurveRisk.Contracts.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace CurveRisk.Api.Tests;

/// <summary>
/// Hosts the real API in process against a private in-memory SQLite database. Nothing is replaced or
/// mocked: requests go through routing, binding, the services, EF Core and the exception handler.
/// The same tests can be pointed at PostgreSQL by configuration.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<ProblemDetailsExceptionHandler>
{
    private readonly string _connectionString = $"Data Source=file:curverisk-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private SqliteConnection? _keepAlive;

    public static readonly QuoteDto[] DemoQuotes =
    [
        new("1Y", 4.05), new("2Y", 3.80), new("3Y", 3.72), new("5Y", 3.78),
        new("7Y", 3.88), new("10Y", 4.02), new("20Y", 4.22), new("30Y", 4.15),
    ];

    public static CreateMarketSnapshotRequest DemoSnapshot { get; } =
        new("USD-SOFR", "2026-09-30", "LogLinearDiscount", DemoQuotes);

    public static CreateTradeRequest Trade(string id) => new(id, 100_000_000m, 3.5, "PayFixed", "5Y", "Client hedge");

    public static async Task<MarketSnapshotResponse> CreateSnapshotAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/api/v1/market-snapshots", DemoSnapshot, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MarketSnapshotResponse>(cancellationToken))!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // A shared-cache in-memory database lives as long as one connection to it stays open.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", ApiSetup.SqliteProvider);
        builder.UseSetting("ConnectionStrings:CurveRisk", _connectionString);
        builder.UseSetting("Database:CreateOnStart", "true");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive?.Dispose();
        }
    }
}
