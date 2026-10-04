using System.Globalization;
using CurveRisk.Copilot;

namespace CurveRisk.Evals;

/// <summary>Command-line settings for an eval run.</summary>
public sealed record EvalSettings
{
    public string Dataset { get; init; } = "evals/datasets/copilot.jsonl";

    public string OutputDirectory { get; init; } = "evals/results";

    public int Trials { get; init; } = 1;

    /// <summary>Minimum overall pass rate. Safety-tagged cases must pass regardless.</summary>
    public double Threshold { get; init; } = 0.9;

    /// <summary>Overrides the provider's model when set.</summary>
    public string? Model { get; init; }

    /// <summary>Overrides the provider's effort level when set.</summary>
    public string? Effort { get; init; }

    /// <summary>Only run cases carrying this tag.</summary>
    public string? Filter { get; init; }

    /// <summary>The provider's options with any command-line overrides applied.</summary>
    public CopilotOptions Apply(CopilotOptions provider) =>
        provider with { Model = Model ?? provider.Model, Effort = Effort ?? provider.Effort };

    /// <summary>Parses <c>--name value</c> pairs. Unknown options are an error, not ignored.</summary>
    public static EvalSettings Parse(IReadOnlyList<string> args)
    {
        if (args.Count % 2 != 0)
        {
            throw new ArgumentException($"Option '{args[^1]}' has no value.");
        }

        var settings = new EvalSettings();
        for (var i = 0; i < args.Count; i += 2)
        {
            settings = settings.With(args[i], args[i + 1]);
        }

        return settings;
    }

    private EvalSettings With(string option, string value) => option switch
    {
        "--dataset" => this with { Dataset = value },
        "--out" => this with { OutputDirectory = value },
        "--trials" => this with { Trials = int.Parse(value, CultureInfo.InvariantCulture) },
        "--threshold" => this with { Threshold = double.Parse(value, CultureInfo.InvariantCulture) },
        "--model" => this with { Model = value },
        "--effort" => this with { Effort = value },
        "--filter" => this with { Filter = value },
        _ => throw new ArgumentException($"Unknown option '{option}'."),
    };
}
