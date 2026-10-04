using CurveRisk.Api.Services;
using CurveRisk.Workflows;
using Microsoft.AspNetCore.RateLimiting;

namespace CurveRisk.Api;

/// <summary>Registration of the risk brief: its service, where paused briefs wait, and how many may run at once.</summary>
public static class RiskBriefSetup
{
    public const string Limiter = "risk-briefs";
    private const int DefaultConcurrentBriefs = 2;

    public static IServiceCollection AddRiskBriefs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RiskBriefService>();

        // In memory: a brief waiting for approval does not survive a restart and is not shared between instances.
        services.AddSingleton<ICheckpointStore<RiskBriefCheckpoint>>(provider =>
            new InMemoryCheckpointStore<RiskBriefCheckpoint>(timeProvider: provider.GetRequiredService<TimeProvider>()));

        // A brief is the most expensive request the API serves: up to 300 engine calls and two model
        // calls. Only a few run at once; the rest are told to come back, not queued behind them.
        var concurrentBriefs = configuration.GetValue("RiskBriefs:MaxConcurrent", DefaultConcurrentBriefs);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddConcurrencyLimiter(Limiter, limiter =>
            {
                limiter.PermitLimit = concurrentBriefs;
                limiter.QueueLimit = 0;
            });
        });
        return services;
    }
}
