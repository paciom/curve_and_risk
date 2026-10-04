using CurveRisk.Api.Services;
using CurveRisk.Copilot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CurveRisk.Api.Tests;

/// <summary>How the Copilot is wired from configuration: provider, budget, and who owns the model client.</summary>
public sealed class CopilotConfigurationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void The_runtime_reads_the_provider_and_budget_from_configuration()
    {
        static CopilotConfiguration Build(params (string Key, string Value)[] settings) => CopilotConfiguration.From(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
                .Build());

        var none = Build();
        var zai = Build(("ZAI_API_KEY", "z"), ("COPILOT_MODEL", "some-model"), ("Copilot:DailyBudgetUsd", "0"));

        Assert.Null(none.Provider);
        Assert.Contains("ANTHROPIC_API_KEY", none.Unavailable, StringComparison.Ordinal);
        Assert.True(none.Budget.HasBudget);
        Assert.Equal(ProviderSettings.ZaiBaseUrl, zai.Provider!.BaseUrl);
        Assert.Equal("some-model", zai.Options.Model);
        Assert.False(zai.Budget.HasBudget);
    }

    [Fact]
    public async Task With_a_provider_configured_the_container_supplies_and_owns_the_model_client()
    {
        await using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ZAI_API_KEY", "z-test");
            builder.UseSetting("COPILOT_MODEL", "some-model");
        });

        var model = configured.Services.GetRequiredService<IModelClient>();

        Assert.IsType<AnthropicProviderClient>(model);
    }
}
