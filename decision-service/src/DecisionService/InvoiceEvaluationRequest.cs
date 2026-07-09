using ApprovalFlow.Contracts;

namespace DecisionService;

public record InvoiceEvaluationRequest(
    string Vendor,
    string Category,
    decimal Total,
    string Description,
    List<InvoiceLineItem> LineItems);
