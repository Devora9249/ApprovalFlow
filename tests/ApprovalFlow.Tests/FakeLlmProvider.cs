using DecisionService;

namespace ApprovalFlow.Tests;

// Configurable ILlmProvider test double — lets tests drive Layer 3 outcomes deterministically,
// including agent-failure paths StubLlmProvider (always auto_approve) can't exercise.
internal class FakeLlmProvider(AgentResult? result = null, AgentException? failure = null) : ILlmProvider
{
    public Task<AgentResult> EvaluateInvoiceAsync(InvoiceEvaluationRequest request)
    {
        if (failure is not null)
            throw failure;

        return Task.FromResult(result ?? new AgentResult(
            Reasoning: "Fake response for testing",
            AmountReasonable: true,
            ItemsConsistentWithCategory: true,
            Confidence: 0.95,
            Recommendation: "auto_approve"));
    }
}
