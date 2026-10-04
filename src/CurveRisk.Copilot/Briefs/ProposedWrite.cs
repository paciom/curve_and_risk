using System.Text.Json;
using CurveRisk.Ai.Tools;

namespace CurveRisk.Copilot.Briefs;

/// <summary>
/// The one change a brief may ask a person to approve: saving a scenario. It is written by graph code
/// from engine results, shown to the person as it stands, and is exactly what runs if they say yes.
/// </summary>
/// <param name="ParallelBp">Shift applied to every zero rate, in basis points.</param>
/// <param name="SteepenerBp">Change in the 2s10s zero spread, in basis points.</param>
public sealed record ProposedWrite(string ScenarioName, double ParallelBp, double SteepenerBp)
{
    public const string Tool = ToolCatalog.SaveScenario;

    public static ProposedWrite ForWorst(ScenarioLine worst) =>
        new($"Risk brief worst case: {worst.Name}", worst.ParallelBp, worst.SteepenerBp);

    public JsonElement ToArguments() =>
        JsonSerializer.SerializeToElement(new { name = ScenarioName, parallelBp = ParallelBp, steepenerBp = SteepenerBp });
}

/// <summary>
/// Carries a person's decision on one proposed write into the tool executor. It approves nothing but
/// that write: a call to another tool, or with other arguments, is declined even when the answer was yes.
/// </summary>
public sealed class ProposalApprovalGate(ProposedWrite proposal, bool approved) : IApprovalGate
{
    public Task<ApprovalDecision> RequestAsync(ToolCallPart toolCall, CancellationToken cancellationToken) =>
        Task.FromResult(Decide(toolCall));

    private ApprovalDecision Decide(ToolCallPart toolCall)
    {
        if (!approved)
        {
            return ApprovalDecision.Deny("The user declined.");
        }

        var isTheProposal = toolCall.Name == ProposedWrite.Tool
            && toolCall.Input.GetRawText() == proposal.ToArguments().GetRawText();
        return isTheProposal ? ApprovalDecision.Approve : ApprovalDecision.Deny("This is not the change the user approved.");
    }
}
