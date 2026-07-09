namespace DecisionService;

public enum PolicyGateOutcome
{
    Duplicate,
    Escalate,
    PassToAgent
}

public sealed record PolicyGateResult(PolicyGateOutcome Outcome, string? Reason)
{
    public static PolicyGateResult PassToAgent() => new(PolicyGateOutcome.PassToAgent, null);
    public static PolicyGateResult Duplicate() => new(PolicyGateOutcome.Duplicate, null);
    public static PolicyGateResult Escalate(string reason) => new(PolicyGateOutcome.Escalate, reason);
}
