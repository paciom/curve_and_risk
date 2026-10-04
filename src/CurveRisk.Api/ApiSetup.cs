using CurveRisk.Api.Endpoints;
using CurveRisk.Api.Persistence;
using CurveRisk.Api.Services;
using CurveRisk.Copilot;
using Microsoft.EntityFrameworkCore;

namespace CurveRisk.Api;

/// <summary>Composition root for the API: everything is registered here and nowhere else.</summary>
public static class ApiSetup
{
    public const string PostgresProvider = "Postgres";
    public const string SqliteProvider = "Sqlite";

    public static IServiceCollection AddCurveRiskApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CurveRiskDbContext>(options => ConfigureDatabase(options, configuration));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RiskRunBacklog>();
        services.AddScoped<MarketSnapshotService>();
        services.AddScoped<TradeService>();
        services.AddScoped<PricingService>();
        services.AddScoped<RiskRunService>();
        services.AddHostedService<RiskRunWorker>();
        AddCopilot(services, configuration);

        services.AddProblemDetails();
        services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        services.AddOpenApi("v1");
        return services;
    }

    /// <summary>The model client is registered only when a provider is configured, and the container owns and disposes it.</summary>
    private static void AddCopilot(IServiceCollection services, IConfiguration configuration)
    {
        var copilot = CopilotConfiguration.From(configuration);
        services.AddSingleton(copilot);
        services.AddScoped<CopilotService>();
        if (copilot.Provider is { } provider)
        {
            services.AddSingleton<IModelClient>(_ => new AnthropicProviderClient(provider));
        }
    }

    public static WebApplication MapCurveRiskApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // The single-page UI in wwwroot. It talks to the API below like any other client.
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapOpenApi();
        app.MapCurveRiskV1();
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok"))).ExcludeFromDescription();
        return app;
    }

    /// <summary>
    /// Creates the schema when asked to. The SQLite provider is the local and demo database, so it is
    /// created on first run unless told otherwise; PostgreSQL is left alone unless Database:CreateOnStart
    /// is set, because a production schema is changed by a deployment step, not by the application.
    /// </summary>
    public static async Task EnsureDatabaseAsync(this WebApplication app)
    {
        var provider = app.Configuration["Database:Provider"] ?? SqliteProvider;
        var isLocal = string.Equals(provider, SqliteProvider, StringComparison.OrdinalIgnoreCase);
        if (!app.Configuration.GetValue("Database:CreateOnStart", defaultValue: isLocal))
        {
            return;
        }

        var scope = app.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<CurveRiskDbContext>().Database.EnsureCreatedAsync().ConfigureAwait(false);
        }
    }

    /// <summary>PostgreSQL in production; SQLite for local runs and tests. Chosen by Database:Provider.</summary>
    public static void ConfigureDatabase(DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? SqliteProvider;
        var connection = configuration.GetConnectionString("CurveRisk");

        if (string.Equals(provider, PostgresProvider, StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(connection ?? throw new InvalidOperationException("ConnectionStrings:CurveRisk is required for the Postgres provider."));
            return;
        }

        if (!string.Equals(provider, SqliteProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown Database:Provider '{provider}'. Use {PostgresProvider} or {SqliteProvider}.");
        }

        options.UseSqlite(connection ?? "Data Source=curverisk.db");
    }
}

public sealed record HealthResponse(string Status);
