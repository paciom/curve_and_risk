using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

public class ProviderSettingsInferenceTests
{
    [Fact]
    public void An_empty_anthropic_key_counts_as_absent_when_inferring_the_provider()
    {
        var settings = Resolve(out var problem, ("ANTHROPIC_API_KEY", string.Empty), ("ZAI_API_KEY", "z-test"), ("COPILOT_MODEL", "some-model"));

        Assert.NotNull(settings);
        Assert.Equal(("z-test", ProviderSettings.ZaiBaseUrl), (settings.ApiKey, settings.BaseUrl));
        Assert.Equal(string.Empty, problem);
    }

    [Fact]
    public void The_zai_endpoint_can_be_overridden()
    {
        var settings = Resolve(
            out _, ("ZAI_API_KEY", "z-test"), ("COPILOT_MODEL", "some-model"), ("COPILOT_BASE_URL", "https://proxy.example/zai"));

        Assert.Equal(new Uri("https://proxy.example/zai"), settings!.BaseUrl);
    }

    [Fact]
    public void An_empty_base_url_override_is_ignored()
    {
        var settings = Resolve(out _, ("ANTHROPIC_API_KEY", "sk-test"), ("COPILOT_BASE_URL", string.Empty));

        Assert.NotNull(settings);
        Assert.Null(settings.BaseUrl);
    }

    private static ProviderSettings? Resolve(out string problem, params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return ProviderSettings.FromVariables(name => map.GetValueOrDefault(name), out problem);
    }
}
