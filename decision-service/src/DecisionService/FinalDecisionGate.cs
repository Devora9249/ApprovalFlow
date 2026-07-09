namespace DecisionService;

public enum FinalGateOutcome
{
    AutoApprove,
    Escalate
}

public sealed record FinalDecisionResult(FinalGateOutcome Outcome, IReadOnlyList<string> Reasons)
{
    public static FinalDecisionResult AutoApprove() => new(FinalGateOutcome.AutoApprove, []);
    public static FinalDecisionResult Escalate(IReadOnlyList<string> reasons) => new(FinalGateOutcome.Escalate, reasons);
}

// Layer 3 — deterministic final gate, run AFTER the AI agent. Pure, sync, no I/O:
// the agent only recommends, this is what always decides (M12).
public static class FinalDecisionGate
{
    public const string LowConfidence = "AUTONOMY-CONFIDENCE";
    public const string AmountNotReasonable = "AMOUNT-NOT-REASONABLE";
    public const string ItemsInconsistent = "ITEMS-INCONSISTENT";

    public static FinalDecisionResult Evaluate(AgentResult agentResult, AutonomySettings settings)
    {
        var reasons = new List<string>();

        if (agentResult.Confidence < settings.ConfidenceThreshold)
            reasons.Add(LowConfidence);

        if (!agentResult.AmountReasonable)
            reasons.Add(AmountNotReasonable);

        if (!agentResult.ItemsConsistentWithCategory)
            reasons.Add(ItemsInconsistent);

        return reasons.Count == 0 ? FinalDecisionResult.AutoApprove() : FinalDecisionResult.Escalate(reasons);
    }
}
