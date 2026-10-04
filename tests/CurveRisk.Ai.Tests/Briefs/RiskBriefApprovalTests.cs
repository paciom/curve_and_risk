using System.Text.Json;
using CurveRisk.Ai.Tools;
using CurveRisk.Copilot;
using CurveRisk.Copilot.Briefs;
using CurveRisk.Workflows;
using static CurveRisk.Ai.Tests.ScriptedModelClient;

namespace CurveRisk.Ai.Tests.Briefs;

/// <summary>
/// The human in the graph. A brief that offers to save its worst scenario stops and waits; the write
/// happens only when the run is resumed through a gate that carries a person's yes to that exact change.
/// </summary>
public class RiskBriefApprovalTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly ProposedWrite Proposal = new("Risk brief worst case: ParallelUp", 50, 0);

    [Fact]
    public async Task A_brief_that_offers_a_save_pauses_with_the_proposal_and_has_written_nothing()
    {
        var engine = StubBook.Engine();

        var run = await Brief.Workflow(new ScriptedModelClient(Says("No figures.")), engine).RunAsync(offerSave: true, Ct);

        Assert.Equal(GraphRunStatus.Paused, run.Status);
        Assert.Equal(RiskBriefGraph.AwaitApproval, run.Outcome);
        Assert.Equal(Proposal, run.State.Proposal);
        Assert.Equal("propose_save", run.Trace[^1].Node);
        Assert.Empty(engine.Saved);
    }

    [Fact]
    public async Task Pausing_does_not_ask_the_gate_because_nothing_has_been_attempted_yet()
    {
        var gate = new RecordingGate(ApprovalDecision.Approve);

        await Brief.Workflow(model: null, StubBook.Engine(), gate).RunAsync(offerSave: true, Ct);

        Assert.Empty(gate.Asked);
    }

    [Fact]
    public async Task The_save_is_offered_whether_or_not_there_is_commentary()
    {
        var run = await Brief.Workflow(model: null, StubBook.Engine()).RunAsync(offerSave: true, Ct);

        Assert.Equal((GraphRunStatus.Paused, CommentaryStatus.NotConfigured), (run.Status, run.State.CommentaryStatus));
        Assert.Equal(["drop_commentary", "propose_save"], run.Trace.TakeLast(2).Select(visit => visit.Node));
    }

    [Fact]
    public async Task Approval_resumes_and_saves_exactly_what_was_proposed()
    {
        var engine = StubBook.Engine();
        var paused = await PausedAsync(engine);

        var run = await Brief.Workflow(model: null, engine, new ProposalApprovalGate(Proposal, approved: true)).ResumeAsync(paused, Ct);

        Assert.Equal(RiskBriefGraph.ScenarioSaved, run.Outcome);
        Assert.Equal(new SavedScenario("S-1", "Risk brief worst case: ParallelUp", new ScenarioShock(50, 0)), Assert.Single(engine.Saved));
        Assert.Equal(engine.Saved[0], run.State.SavedScenario);
    }

    [Fact]
    public async Task Resuming_continues_from_the_checkpoint_not_from_the_start()
    {
        var engine = StubBook.Engine();
        var paused = await PausedAsync(engine);
        var model = new ScriptedModelClient();

        var run = await Brief.Workflow(model, engine, new ProposalApprovalGate(Proposal, approved: true)).ResumeAsync(paused, Ct);

        // One node, one more tool call, no model call: nothing before the pause ran again.
        Assert.Equal(["save_scenario"], run.Trace.Select(visit => visit.Node));
        Assert.Equal(paused.ToolCalls.Count + 1, run.State.ToolCalls.Count);
        Assert.Equal("save_scenario", run.State.ToolCalls[^1].Name);
        Assert.Equal(ToolOutcome.Succeeded, run.State.ToolCalls[^1].Outcome);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task A_declined_approval_resumes_and_ends_with_nothing_saved()
    {
        var engine = StubBook.Engine();
        var paused = await PausedAsync(engine);

        var run = await Brief.Workflow(model: null, engine, new ProposalApprovalGate(Proposal, approved: false)).ResumeAsync(paused, Ct);

        Assert.Equal(RiskBriefGraph.SaveDeclined, run.Outcome);
        Assert.Equal((ToolOutcome.Denied, "Not executed. The user declined."), (run.State.ToolCalls[^1].Outcome, run.State.ToolCalls[^1].Result));
        Assert.Empty(engine.Saved);
        Assert.Null(run.State.SavedScenario);
        Assert.Null(run.State.EngineError);
    }

    [Fact]
    public async Task Resuming_without_anyone_having_decided_writes_nothing()
    {
        var engine = StubBook.Engine();
        var paused = await PausedAsync(engine);

        var run = await Brief.Workflow(model: null, engine).ResumeAsync(paused, Ct);

        Assert.Equal(RiskBriefGraph.SaveDeclined, run.Outcome);
        Assert.Empty(engine.Saved);
    }

    [Fact]
    public async Task A_yes_to_one_proposal_does_not_approve_a_state_that_proposes_something_else()
    {
        var engine = StubBook.Engine();
        var paused = await PausedAsync(engine);
        var tampered = paused with { Proposal = Proposal with { ParallelBp = 500 } };

        var run = await Brief.Workflow(model: null, engine, new ProposalApprovalGate(Proposal, approved: true)).ResumeAsync(tampered, Ct);

        Assert.Equal(RiskBriefGraph.SaveDeclined, run.Outcome);
        Assert.Equal("Not executed. This is not the change the user approved.", run.State.ToolCalls[^1].Result);
        Assert.Empty(engine.Saved);
    }

    [Fact]
    public async Task Resuming_a_state_with_no_proposal_makes_no_tool_call()
    {
        var engine = StubBook.Engine();

        var run = await Brief.Workflow(model: null, engine, new ProposalApprovalGate(Proposal, approved: true)).ResumeAsync(new RiskBriefState(), Ct);

        Assert.Equal(RiskBriefGraph.SaveDeclined, run.Outcome);
        Assert.Empty(run.State.ToolCalls);
        Assert.Empty(engine.Saved);
    }

    [Theory]
    [InlineData("save_scenario", "Risk brief worst case: ParallelUp", 50, true)]
    [InlineData("save_scenario", "Something else", 50, false)]
    [InlineData("save_scenario", "Risk brief worst case: ParallelUp", 51, false)]
    [InlineData("run_scenario", "Risk brief worst case: ParallelUp", 50, false)]
    public async Task The_gate_approves_only_the_proposed_tool_with_the_proposed_arguments(string tool, string name, double parallelBp, bool approved)
    {
        var call = new ToolCallPart("call_0", tool, JsonSerializer.SerializeToElement(new { name, parallelBp, steepenerBp = 0.0 }));

        var decision = await new ProposalApprovalGate(Proposal, approved: true).RequestAsync(call, Ct);

        Assert.Equal(approved, decision.Approved);
    }

    [Fact]
    public void The_proposal_is_the_worst_scenario_under_a_name_the_graph_wrote()
    {
        var proposal = ProposedWrite.ForWorst(new ScenarioLine("Flattener", 0, -50, -1234.5));

        Assert.Equal(new ProposedWrite("Risk brief worst case: Flattener", 0, -50), proposal);
        Assert.Equal("""{"name":"Risk brief worst case: Flattener","parallelBp":0,"steepenerBp":-50}""", proposal.ToArguments().GetRawText());
    }

    private static async Task<RiskBriefState> PausedAsync(IRiskEngine engine)
    {
        var run = await Brief.Workflow(model: null, engine).RunAsync(offerSave: true, Ct);
        Assert.Equal(GraphRunStatus.Paused, run.Status);
        return run.State;
    }
}
