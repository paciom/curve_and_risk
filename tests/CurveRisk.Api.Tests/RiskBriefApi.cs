using System.Net.Http.Json;
using System.Text.Json;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

/// <summary>What the risk brief tests share: booking a book, asking for a brief, reading a problem.</summary>
internal static class RiskBriefApi
{
    public const string Briefs = "/api/v1/risk-briefs";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    public static async Task<Guid> SnapshotWithTradesAsync(HttpClient client, params string[] tradeIds)
    {
        var snapshot = await ApiFactory.CreateSnapshotAsync(client, Ct);
        foreach (var tradeId in tradeIds)
        {
            (await client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade(tradeId), Ct)).EnsureSuccessStatusCode();
        }

        return snapshot.Id;
    }

    public static async Task<RiskBriefResponse> CreateAsync(HttpClient client, CreateRiskBriefRequest request)
    {
        var response = await client.PostAsJsonAsync(Briefs, request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RiskBriefResponse>(Ct))!;
    }

    public static async Task<string?> ProblemTypeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("type").GetString();
}
