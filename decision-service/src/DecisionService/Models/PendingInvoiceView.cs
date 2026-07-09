namespace DecisionService;

public record PendingInvoiceView(
    string InvoiceId,
    string Vendor,
    string Category,
    decimal Total,
    string? AgentRecommendation,
    string? AgentReasoning,
    double? AgentConfidence,
    List<string> PolicyViolations,
    DateTime? EscalatedAt);
