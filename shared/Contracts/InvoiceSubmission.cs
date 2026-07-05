namespace ApprovalFlow.Contracts;

public record InvoiceSubmission(
    string Submitter,
    string Vendor,
    string InvoiceNumber,
    string Category,
    decimal Total,
    decimal TaxAmount,
    bool ReceiptPresent,
    string Description,
    List<InvoiceLineItem> LineItems);
