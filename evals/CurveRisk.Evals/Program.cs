using CurveRisk.Copilot;
using CurveRisk.Evals;

// Usage: dotnet run --project evals/CurveRisk.Evals -- [--dataset path] [--out dir] [--trials n]
//                                                      [--threshold 0..1] [--model id] [--effort level] [--filter tag]
// Provider: ANTHROPIC_API_KEY, or ZAI_API_KEY with COPILOT_MODEL for z.ai's compatible endpoint.
// Exit codes: 0 pass, 1 below threshold or any run of a case tagged injection/writes failed, 2 could not run.
// This calls a live model and costs money.

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

var provider = ProviderSettings.FromVariables(Environment.GetEnvironmentVariable, out var problem);
if (provider is null)
{
    await Console.Error.WriteLineAsync($"{problem} Evals were not run.");
    return EvalCli.CouldNotRun;
}

var options = settings.Apply(provider.Options);
using var model = new AnthropicProviderClient(provider, options);

var console = new EvalConsole(Console.Out, Console.Error);
return await new EvalCli(model, options, console, TimeProvider.System).RunAsync(settings);
