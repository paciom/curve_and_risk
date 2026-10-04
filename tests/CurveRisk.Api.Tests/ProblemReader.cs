using System.Net.Http.Json;
using System.Text.Json;

namespace CurveRisk.Api.Tests;

/// <summary>Reads the RFC 9457 problem body of a response, so a test can state the exact text a caller sees.</summary>
internal static class ProblemReader
{
    public const string TypePrefix = "https://curverisk.example/problems/";

    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

    public static string? Text(this JsonElement problem, string property) => problem.GetProperty(property).GetString();

    /// <summary>The messages reported for one field of a validation problem.</summary>
    public static string?[] ErrorsFor(this JsonElement problem, string field) =>
        [.. problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(message => message.GetString())];
}
