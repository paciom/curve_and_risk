using System.Net;
using System.Net.Http.Json;
using CurveRisk.Contracts.V1;

namespace CurveRisk.Api.Tests;

/// <summary>
/// The approval round trip over HTTP: a brief pauses and hands out an id, and the write happens only
/// when a decision is posted to that id, once.
/// </summary>
public sealed class RiskBriefApprovalApiTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_brief_that_offers_a_save_waits_and_says_exactly_what_it_would_save()
    {
        var (_, brief) = await PausedAsync();

        Assert.Equal("AwaitingApproval", brief.Status);
        Assert.Matches("^[0-9a-f]{32}$", brief.PendingApproval!.ApprovalId);
        Assert.Equal("Risk brief worst case: ParallelDown", brief.PendingApproval.ScenarioName);
        Assert.Equal((-50, 0), (brief.PendingApproval.ParallelBp, brief.PendingApproval.SteepenerBp));
        Assert.Equal("propose_save", brief.Trace[^1].Node);
        Assert.Null(brief.SavedScenarioId);
        Assert.NotNull(brief.Figures);
    }

    [Fact]
    public async Task Approving_saves_the_scenario_and_the_trace_continues_from_where_it_paused()
    {
        var (client, paused) = await PausedAsync();

        var brief = await DecideAsync(client, paused.PendingApproval!.ApprovalId, approved: true);

        // The id is the engine's own, for this request: the API does not store scenarios yet.
        Assert.Equal(("ScenarioSaved", "S-0001"), (brief.Status, brief.SavedScenarioId));
        Assert.Null(brief.PendingApproval);
        Assert.Equal([.. paused.Trace.Select(visit => visit.Node), "save_scenario"], brief.Trace.Select(visit => visit.Node));
        Assert.Equal(paused.Figures!.TotalPresentValue, brief.Figures!.TotalPresentValue);
    }

    [Fact]
    public async Task Declining_ends_the_brief_with_nothing_saved()
    {
        var (client, paused) = await PausedAsync();

        var brief = await DecideAsync(client, paused.PendingApproval!.ApprovalId, approved: false);

        Assert.Equal("SaveDeclined", brief.Status);
        Assert.Null(brief.SavedScenarioId);
    }

    [Fact]
    public async Task A_decision_that_does_not_say_yes_is_a_no()
    {
        var (client, paused) = await PausedAsync();

        var response = await client.PostAsJsonAsync(ApprovalUrl(paused.PendingApproval!.ApprovalId), new { }, Ct);
        var brief = await response.Content.ReadFromJsonAsync<RiskBriefResponse>(Ct);

        Assert.Equal("SaveDeclined", brief!.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_approval_id_works_once_whatever_the_first_decision_was(bool firstDecision)
    {
        var (client, paused) = await PausedAsync();
        var approvalId = paused.PendingApproval!.ApprovalId;
        await DecideAsync(client, approvalId, firstDecision);

        var again = await client.PostAsJsonAsync(ApprovalUrl(approvalId), new RiskBriefApprovalRequest(true), Ct);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.EndsWith("/not-found", await RiskBriefApi.ProblemTypeAsync(again), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_approval_id_nobody_was_given_is_a_404()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync(ApprovalUrl("0123456789abcdef0123456789abcdef"), new RiskBriefApprovalRequest(true), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_brief_that_was_not_asked_to_offer_a_save_has_nothing_to_approve()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "A-1");

        var brief = await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId, OfferSave: false));

        Assert.Null(brief.PendingApproval);
        Assert.DoesNotContain(brief.Trace, visit => visit.Node == "propose_save");
    }

    private static string ApprovalUrl(string approvalId) => $"/api/v1/risk-briefs/approvals/{approvalId}";

    private async Task<(HttpClient Client, RiskBriefResponse Brief)> PausedAsync()
    {
        var client = _factory.CreateClient();
        var snapshotId = await RiskBriefApi.SnapshotWithTradesAsync(client, "A-1");
        return (client, await RiskBriefApi.CreateAsync(client, new CreateRiskBriefRequest(snapshotId, OfferSave: true)));
    }

    private static async Task<RiskBriefResponse> DecideAsync(HttpClient client, string approvalId, bool approved)
    {
        var response = await client.PostAsJsonAsync(ApprovalUrl(approvalId), new RiskBriefApprovalRequest(approved), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RiskBriefResponse>(Ct))!;
    }
}
