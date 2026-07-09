namespace DecisionService;

// Thrown by ILlmProvider implementations instead of ever returning null (M15) — lets
// InvoiceProcessor fail fast and escalate rather than silently treating "no result" as approval.
public class AgentException(string message, Exception? innerException = null)
    : Exception(message, innerException);
