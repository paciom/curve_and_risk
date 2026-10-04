using CurveRisk.Copilot;
using CurveRisk.Evals;

namespace CurveRisk.Ai.Tests;

/// <summary>Command-line settings: defaults, overrides of the provider's options, and rejected input.</summary>
public class EvalSettingsTests
{
    [Fact]
    public void No_arguments_give_the_checked_in_dataset_one_trial_and_a_ninety_percent_threshold()
    {
        var settings = EvalSettings.Parse([]);

        Assert.Equal(
            ("evals/datasets/copilot.jsonl", "evals/results", 1, 0.9),
            (settings.Dataset, settings.OutputDirectory, settings.Trials, settings.Threshold));
        Assert.Null(settings.Model);
        Assert.Null(settings.Effort);
        Assert.Null(settings.Filter);
    }

    [Fact]
    public void Without_overrides_the_providers_model_and_effort_are_kept()
    {
        var provider = new CopilotOptions { Model = "provider-model", Effort = "low" };

        var applied = new EvalSettings().Apply(provider);

        Assert.Equal(("provider-model", "low"), (applied.Model, applied.Effort));
    }

    [Fact]
    public void Overrides_replace_the_providers_model_and_effort_and_nothing_else()
    {
        var provider = new CopilotOptions { Model = "provider-model", Effort = "low", MaxModelCalls = 7 };

        var applied = new EvalSettings { Model = "m", Effort = "max" }.Apply(provider);

        Assert.Equal(provider with { Model = "m", Effort = "max" }, applied);
    }

    [Fact]
    public void An_option_without_a_value_is_rejected_by_name()
    {
        var error = Assert.Throws<ArgumentException>(() => EvalSettings.Parse(["--trials", "2", "--filter"]));

        Assert.Equal("Option '--filter' has no value.", error.Message);
    }

    [Fact]
    public void An_unknown_option_is_rejected_by_name()
    {
        var error = Assert.Throws<ArgumentException>(() => EvalSettings.Parse(["--trials", "2", "--nope", "1"]));

        Assert.Equal("Unknown option '--nope'.", error.Message);
    }

    [Fact]
    public void A_later_option_overrides_an_earlier_one()
    {
        var settings = EvalSettings.Parse(["--trials", "2", "--trials", "5"]);

        Assert.Equal(5, settings.Trials);
    }
}
