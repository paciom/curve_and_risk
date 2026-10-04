using System.Net;
using System.Net.Http.Json;
using CurveRisk.Contracts.V1;
using CurveRisk.Copilot.Briefs;

namespace CurveRisk.Api.Tests;

/// <summary>
/// The risk brief over HTTP with no model provider configured. The graph, the tools, the engine and
/// the database are real. Each test has its own database, because a brief covers the whole book.
/// </summary>
public sealed class RiskBriefApiTests : IDisposable
{
    private const string Briefs = RiskBriefApi.Briefs;
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string[] EnginePath = ["load_portfolio", "price_trades", "run_risk", "run_scenarios", "gather", "find_flags"];
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Without_a_model_provider_the_brief_still_returns_the_engine_figures()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "B-1");

        var brief = await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId));

        Assert.Equal("WithoutCommentary", brief.Status);
        Assert.Equal(("NotConfigured", ""), (brief.Commentary.Status, brief.Commentary.Text));
        Assert.Empty(brief.Commentary.UngroundedFigures);
        Assert.Equal(new ModelUsageDto(0, 0, 0, 0m), brief.Usage);
        Assert.Equal([.. EnginePath, "narrate", "drop_commentary"], brief.Trace.Select(visit => visit.Node));
        Assert.All(brief.Trace, visit => Assert.Equal("Completed", visit.Result));
    }

    [Fact]
    public async Task The_figures_are_the_ones_the_valuation_endpoint_gives_for_the_same_trade()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "B-1");

        var figures = (await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId))).Figures!;

        Assert.Equal(("USD", 1_270_062.29), (figures.Currency, figures.TotalPresentValue));
        Assert.Equal("B-1", Assert.Single(figures.Trades).TradeId);
        Assert.Equal(figures.TotalParallelDv01, figures.Trades[0].ParallelDv01);
        Assert.Equal(["ParallelUp", "ParallelDown", "Steepener", "Flattener"], figures.Scenarios.Select(scenario => scenario.Name));
        Assert.Equal(2_276_468.21, figures.Scenarios[0].ProfitAndLoss);
        Assert.Equal("B-1", figures.LargestDv01TradeId);
        Assert.Contains(figures.MostExposedBucketTenorYears, figures.Buckets.Select(bucket => bucket.TenorYears));
    }

    [Fact]
    public async Task Two_trades_give_totals_that_are_the_sum_of_the_two()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "B-1", "B-2");

        var figures = (await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId))).Figures!;

        Assert.Equal(2_540_124.58, figures.TotalPresentValue);
        Assert.Equal(Math.Round(figures.Trades.Sum(trade => trade.ParallelDv01), 2), figures.TotalParallelDv01);
    }

    [Fact]
    public async Task An_empty_book_is_a_brief_with_no_figures_not_an_error()
    {
        var client = _factory.CreateClient();
        var snapshot = await ApiFactory.CreateSnapshotAsync(client, Ct);

        var brief = await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshot.Id));

        Assert.Equal("EmptyPortfolio", brief.Status);
        Assert.Null(brief.Figures);
        Assert.Equal("load_portfolio", Assert.Single(brief.Trace).Node);
    }

    [Fact]
    public async Task An_unknown_snapshot_is_a_404()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(Briefs, new CreateRiskBriefRequest(Guid.NewGuid()), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.EndsWith("/not-found", await RiskBriefApi.ProblemTypeAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_book_larger_than_a_brief_covers_is_a_422_instead_of_a_brief_of_part_of_it()
    {
        var client = _factory.CreateClient();
        var ids = Enumerable.Range(0, 51).Select(index => $"L-{index:D2}").ToArray();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, ids);

        var response = await client.PostAsJsonAsync(Briefs, new CreateRiskBriefRequest(snapshotId), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.EndsWith("/book-too-large", await RiskBriefApi.ProblemTypeAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_does_not_mention_saving_does_not_pause()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "B-1");

        var response = await client.PostAsJsonAsync(Briefs, new { snapshotId }, Ct);
        var brief = await response.Content.ReadFromJsonAsync<RiskBriefResponse>(Ct);

        Assert.Equal("WithoutCommentary", brief!.Status);
        Assert.Null(brief.PendingApproval);
    }

    [Fact]
    public async Task The_graph_endpoint_serves_the_structure_of_the_definition_that_runs()
    {
        var graph = await _factory.CreateClient().GetFromJsonAsync<GraphResponse>($"{Briefs}/graph", Ct);

        var shape = RiskBriefWorkflow.Shape;
        Assert.Equal("risk-brief", graph!.Name);
        Assert.Equal(shape.Nodes.Select(node => new GraphNodeDto(node.Name, node.Kind.ToString())), graph.Nodes);
        Assert.Equal(shape.Edges.Select(edge => new GraphEdgeDto(edge.From, edge.To, edge.Label)), graph.Edges);
        Assert.Contains(new GraphNodeDto("await_approval", "Pause"), graph.Nodes);
        Assert.Contains(new GraphEdgeDto("check_grounding", "request_repair", "ungrounded, repair left"), graph.Edges);
    }
}
