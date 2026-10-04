using Anthropic;
using CurveRisk.Copilot;
using CurveRisk.Evals;

// Usage: dotnet run --project evals/CurveRisk.Evals -- [--dataset path] [--out dir] [--trials n]
//                                                      [--threshold 0..1] [--model id] [--effort level] [--filter tag]
// Exit codes: 0 pass, 1 below threshold or any run of a case tagged injection/writes failed, 2 could not run.
// This calls the live model and costs money; the estimated spend is printed at the end.

EvalSettings settings;
try
{
    settings = EvalSettings.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return EvalCli.CouldNotRun;
}

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")))
{
    await Console.Error.WriteLineAsync("No Anthropic credentials found (ANTHROPIC_API_KEY or ANTHROPIC_AUTH_TOKEN). Evals were not run.");
    return EvalCli.CouldNotRun;
}

using var client = new AnthropicClient();
var model = new AnthropicModelClient(client, settings.ToCopilotOptions());
return await new EvalCli(model, Console.Out, Console.Error, TimeProvider.System).RunAsync(settings);
