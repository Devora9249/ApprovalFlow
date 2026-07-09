namespace DecisionService;

public interface ILlmProvider
{
    Task<AgentResult> EvaluateInvoiceAsync(InvoiceEvaluationRequest request);
}
