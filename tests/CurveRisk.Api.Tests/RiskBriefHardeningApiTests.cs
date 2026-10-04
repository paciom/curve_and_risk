using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CurveRisk.Contracts.V1;
using CurveRisk.Copilot;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CurveRisk.Api.Tests;

/// <summary>
/// What independent reviews of the brief found over HTTP, kept as tests: caller data that produced a
/// 500, an approval that was lost when the decision failed, and no limit on how many briefs run at once.
/// </summary>
public sealed class RiskBriefHardeningApiTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Two_trade_ids_that_differ_only_by_case_are_a_422_not_a_500()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "K-1", "k-1");

        var response = await client.PostAsJsonAsync(RiskBriefApi.Briefs, new CreateRiskBriefRequest(snapshotId), Ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.EndsWith("/unprocessable", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("The engine returned figures for a different trade", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trade_the_engine_cannot_price_is_a_422_as_it_is_on_the_valuation_route()
    {
        var client = _factory.CreateClient();
        var snapshot = await ApiFactory.CreateSnapshotAsync(client, Ct);
        var unpriceable = new CreateTradeRequest("X-1", 100_000_000m, 3.5, "PayFixed", "9999999Y", "Too long");
        (await client.PostAsJsonAsync("/api/v1/trades", unpriceable, Ct)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(RiskBriefApi.Briefs, new CreateRiskBriefRequest(snapshot.Id), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.EndsWith("/unprocessable", await RiskBriefApi.ProblemTypeAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_book_that_grows_past_the_limit_after_the_pause_does_not_cost_the_approval()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "G-00");
        var paused = await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId, OfferSave: true));
        foreach (var index in Enumerable.Range(1, 50))
        {
            (await client.PostAsJsonAsync("/api/v1/trades", ApiFactory.Trade($"G-{index:D2}"), Ct)).EnsureSuccessStatusCode();
        }

        var decision = await client.PostAsJsonAsync(ApprovalUrl(paused.PendingApproval!.ApprovalId), new RiskBriefApprovalRequest(true), Ct);
        var brief = await decision.Content.ReadFromJsonAsync<RiskBriefResponse>(Ct);

        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Equal("ScenarioSaved", brief!.Status);
    }

    [Fact]
    public async Task An_approval_id_nobody_was_given_is_a_not_found_problem_that_names_no_brief()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(ApprovalUrl("0123456789abcdef0123456789abcdef"), new RiskBriefApprovalRequest(true), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.EndsWith("/not-found", await RiskBriefApi.ProblemTypeAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task While_the_allowed_number_of_briefs_is_running_another_is_told_to_come_back()
    {
        var model = new HeldModel();
        var client = _factory
            .WithWebHostBuilder(builder => builder
                .UseSetting("RiskBriefs:MaxConcurrent", "1")
                .ConfigureTestServices(services => services.RemoveAll<IModelClient>().AddSingleton<IModelClient>(model)))
            .CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "R-1");

        var first = client.PostAsJsonAsync(RiskBriefApi.Briefs, new CreateRiskBriefRequest(snapshotId), Ct);
        await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var second = await client.PostAsJsonAsync(RiskBriefApi.Briefs, new CreateRiskBriefRequest(snapshotId), Ct);
        model.Release.SetResult();

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
    }

    [Fact]
    public async Task Once_a_brief_has_finished_the_next_one_is_served()
    {
        var client = _factory.WithWebHostBuilder(builder => builder.UseSetting("RiskBriefs:MaxConcurrent", "1")).CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "R-1");

        await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId));
        var again = await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId));

        Assert.Equal("WithoutCommentary", again.Status);
    }

    private static string ApprovalUrl(string approvalId) => $"{RiskBriefApi.Briefs}/approvals/{approvalId}";

    /// <summary>A model whose answer does not arrive until the test lets it, so a brief can be held in flight.</summary>
    private sealed class HeldModel : IModelClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new ModelResponse([new TextPart("No figures.")], StopReason.EndTurn, TokenUsage.Zero, "held");
        }
    }
}
