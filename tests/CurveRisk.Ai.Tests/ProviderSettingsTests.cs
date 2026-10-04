using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

public class ProviderSettingsTests
{
    [Fact]
    public void Anthropic_is_the_default_and_keeps_the_default_options()
    {
        var settings = Resolve(out var problem, ("ANTHROPIC_API_KEY", "sk-test"));

        Assert.NotNull(settings);
        Assert.Empty(problem);
        Assert.Equal(("sk-test", null), (settings.ApiKey, settings.BaseUrl));
        Assert.Equal(new CopilotOptions(), settings.Options);
    }

    [Fact]
    public void A_zai_key_alone_selects_zai_with_its_endpoint_and_conservative_options()
    {
        var settings = Resolve(out _, ("ZAI_API_KEY", "z-test"), ("COPILOT_MODEL", "some-model"));

        Assert.NotNull(settings);
        Assert.Equal(ProviderSettings.ZaiBaseUrl, settings.BaseUrl);
        Assert.Equal("some-model", settings.Options.Model);
        Assert.Null(settings.Options.Effort);
        Assert.False(settings.Options.PromptCaching);
        Assert.Equal(0m, settings.Options.Pricing.CostOf(new TokenUsage(1_000_000, 1_000_000, 0, 0)));
    }

    [Fact]
    public void An_explicit_provider_wins_when_both_keys_are_present()
    {
        var settings = Resolve(
            out _, ("COPILOT_PROVIDER", "ZAI"), ("ANTHROPIC_API_KEY", "sk"), ("ZAI_API_KEY", "z"), ("COPILOT_MODEL", "m"));

        Assert.Equal("z", settings!.ApiKey);
    }

    [Fact]
    public void Model_and_base_url_can_be_overridden()
    {
        var settings = Resolve(
            out _, ("ANTHROPIC_API_KEY", "sk"), ("COPILOT_MODEL", "claude-sonnet-5-5"), ("COPILOT_BASE_URL", "https://proxy.example/v1"));

        Assert.Equal("claude-sonnet-5-5", settings!.Options.Model);
        Assert.Equal(new Uri("https://proxy.example/v1"), settings.BaseUrl);
    }

    [Theory]
    [InlineData("anthropic", "ANTHROPIC_API_KEY is not set.")]
    [InlineData("zai", "needs both ZAI_API_KEY and COPILOT_MODEL")]
    [InlineData("other", "Unknown COPILOT_PROVIDER")]
    public void Missing_or_unknown_configuration_is_explained(string provider, string expected)
    {
        var settings = Resolve(out var problem, ("COPILOT_PROVIDER", provider));

        Assert.Null(settings);
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Zai_without_a_model_name_is_refused_rather_than_guessed()
    {
        Assert.Null(Resolve(out _, ("ZAI_API_KEY", "z")));
    }

    private static ProviderSettings? Resolve(out string problem, params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return ProviderSettings.FromVariables(name => map.GetValueOrDefault(name), out problem);
    }
}
