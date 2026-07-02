namespace ApprovalFlow.Contracts;

public record InvoiceSubmittedEvent(
    string InvoiceId,
    string CorrelationId,
    DateTime SubmittedAt,
    InvoiceSubmission Invoice);
