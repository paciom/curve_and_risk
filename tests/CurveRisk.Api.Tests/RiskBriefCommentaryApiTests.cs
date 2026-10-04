using System.Net.Http.Json;
using System.Text.Json;
using CurveRisk.Contracts.V1;
using CurveRisk.Copilot;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CurveRisk.Api.Tests;

/// <summary>The commentary step over HTTP, with the model replaced by a script and everything else real.</summary>
public sealed class RiskBriefCommentaryApiTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Commentary_that_quotes_the_figures_is_returned_with_what_it_cost()
    {
        var client = ClientWith(Says("Total PV is USD 1,270,062.29."));
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "B-1");

        var brief = await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId));

        Assert.Equal("Completed", brief.Status);
        Assert.Equal(("Grounded", "Total PV is USD 1,270,062.29."), (brief.Commentary.Status, brief.Commentary.Text));
        Assert.Equal(new ModelUsageDto(1, 1000, 100, 0.006m), brief.Usage);
        Assert.Equal("accept_commentary", brief.Trace[^1].Node);
    }

    [Fact]
    public async Task Commentary_with_an_invented_figure_is_left_out_and_the_draft_never_reaches_the_caller()
    {
        var client = ClientWith(Says("Total PV is USD 9,876,543."), Says("Total PV is USD 9,876,543."));
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "B-1");

        var response = await client.PostAsJsonAsync(RiskBriefApi.Briefs, new CreateRiskBriefRequest(snapshotId), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var brief = JsonSerializer.Deserialize<RiskBriefResponse>(body, JsonSerializerOptions.Web)!;

        Assert.Equal(("WithoutCommentary", "Withheld", ""), (brief.Status, brief.Commentary.Status, brief.Commentary.Text));
        Assert.Equal(["USD 9,876,543"], brief.Commentary.UngroundedFigures);
        Assert.DoesNotContain("Total PV is USD 9,876,543", body, StringComparison.Ordinal);
        Assert.NotNull(brief.Figures);
    }

    private HttpClient ClientWith(params ModelResponse[] script) => _factory
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IModelClient>();
            services.AddSingleton<IModelClient>(new Scripted(script));
        }))
        .CreateClient();

    private static ModelResponse Says(string text) =>
        new([new TextPart(text)], StopReason.EndTurn, new TokenUsage(1000, 100, 0, 0), "scripted");

    private sealed class Scripted(ModelResponse[] script) : IModelClient
    {
        private int _next;

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(script[Interlocked.Increment(ref _next) - 1]);
    }
}
