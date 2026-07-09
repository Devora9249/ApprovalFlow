namespace DecisionService;

// Used in CI and tests — never calls the real Gemini API (M16).
public class StubLlmProvider : ILlmProvider
{
    public Task<AgentResult> EvaluateInvoiceAsync(InvoiceEvaluationRequest request) =>
        Task.FromResult(new AgentResult(
            Reasoning: "Stub response for testing",
            AmountReasonable: true,
            ItemsConsistentWithCategory: true,
            Confidence: 0.95,
            Recommendation: "auto_approve"));
}
