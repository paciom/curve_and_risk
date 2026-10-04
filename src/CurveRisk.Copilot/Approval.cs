namespace CurveRisk.Copilot;

public sealed record ApprovalDecision(bool Approved, string Reason)
{
    public static ApprovalDecision Approve { get; } = new(true, "Approved by user.");

    public static ApprovalDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Human-in-the-loop control for tools that change state. This, not the system prompt, is what stops a
/// prompt-injected model from writing: the model can ask, but only a person can say yes.
/// </summary>
public interface IApprovalGate
{
    Task<ApprovalDecision> RequestAsync(ToolCallPart toolCall, CancellationToken cancellationToken);
}

/// <summary>Safe default for hosts with nobody to ask (batch runs, evals).</summary>
public sealed class DenyAllApprovalGate : IApprovalGate
{
    public Task<ApprovalDecision> RequestAsync(ToolCallPart toolCall, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalDecision.Deny("No user is available to approve changes in this session."));
}
