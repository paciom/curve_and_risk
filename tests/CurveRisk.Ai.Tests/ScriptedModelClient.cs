using System.Text.Json;
using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

/// <summary>Plays back a fixed sequence of model responses and records every request it was sent.</summary>
internal sealed class ScriptedModelClient(params Func<ModelRequest, ModelResponse>[] script) : IModelClient
{
    private readonly Queue<Func<ModelRequest, ModelResponse>> _script = new(script);

    public List<ModelRequest> Requests { get; } = [];

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        // Snapshot the turns: the agent keeps appending to the same list after this call returns.
        Requests.Add(request with { Turns = [.. request.Turns] });
        return _script.Count == 0
            ? throw new InvalidOperationException("The script ran out of responses.")
            : Task.FromResult(_script.Dequeue()(request));
    }

    public static Func<ModelRequest, ModelResponse> Says(string text) =>
        _ => new ModelResponse([new TextPart(text)], StopReason.EndTurn, Usage, "scripted");

    public static Func<ModelRequest, ModelResponse> Calls(params (string Name, object Args)[] calls) =>
        _ => new ModelResponse(
            [.. calls.Select((c, i) => (ContentPart)new ToolCallPart($"call_{i}", c.Name, JsonSerializer.SerializeToElement(c.Args)))],
            StopReason.ToolUse,
            Usage,
            "scripted");

    public static Func<ModelRequest, ModelResponse> Stops(StopReason reason) =>
        _ => new ModelResponse([], reason, Usage, "scripted");

    /// <summary>Answers using the content of the most recent successful tool result, via <paramref name="render"/>.</summary>
    public static Func<ModelRequest, ModelResponse> SaysFromLastResult(Func<JsonElement, string> render) =>
        request =>
        {
            var result = request.Turns.SelectMany(t => t.Parts).OfType<ToolResultPart>().Last(r => !r.IsError);
            using var document = JsonDocument.Parse(result.Content);
            return new ModelResponse([new TextPart(render(document.RootElement))], StopReason.EndTurn, Usage, "scripted");
        };

    private static TokenUsage Usage => new(InputTokens: 1000, OutputTokens: 100, CacheReadTokens: 0, CacheWriteTokens: 0);
}
