using ApprovalFlow.Contracts;
using DecisionService;

namespace ApprovalFlow.Tests;

// Shared defaults for building test invoices/settings — every test overrides only the field(s) it cares about.
internal static class InvoiceTestData
{
    public static AutonomySettings DefaultSettings() => new()
    {
        CeilingAmount = 500m,
        ConfidenceThreshold = 0.80,
        WhiteListCategoriesRaw = "office_supplies,business_meals,transportation,software,hardware",
        KnownVendorsRaw = "Staples,Bistro 19,Uber"
    };

    public static InvoiceSubmission ValidInvoice(
        string vendor = "Staples",
        string invoiceNumber = "INV-1001",
        string category = "office_supplies",
        decimal total = 120.00m,
        decimal taxAmount = 20.00m,
        bool receiptPresent = true,
        List<InvoiceLineItem>? lineItems = null) =>
        new(
            Submitter: "dana.cohen@clearspend.example",
            Vendor: vendor,
            InvoiceNumber: invoiceNumber,
            Category: category,
            Total: total,
            TaxAmount: taxAmount,
            ReceiptPresent: receiptPresent,
            Description: "Office restock",
            LineItems: lineItems ?? [new InvoiceLineItem("Paper + pens", 100.00m)]);
}
