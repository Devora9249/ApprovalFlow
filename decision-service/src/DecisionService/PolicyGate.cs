using System.Globalization;
using ApprovalFlow.Contracts;

namespace DecisionService;

// Pure, sync, no Dapr I/O — this is what M12's unit tests exercise directly.
// The duplicate check (which needs the state store) lives in InvoiceProcessor.
public static class PolicyGate
{
    public const string MathMismatch = "GLOBAL-MATH";
    public const string MissingReceipt = "GLOBAL-RECEIPT";
    public const string UnknownVendor = "GLOBAL-VENDOR";
    public const string AutonomyCeiling = "AUTONOMY-CEILING";
    public const string CategoryNotAllowed = "CATEGORY-NOT-ALLOWED";

    private const decimal ReceiptRequiredAboveAmount = 25m;

    public static PolicyGateResult EvaluateDeterministicChecks(InvoiceSubmission invoice, AutonomySettings settings)
    {
        var lineItemTotal = invoice.LineItems.Sum(item => item.Total);
        if (lineItemTotal + invoice.TaxAmount != invoice.Total)
            return PolicyGateResult.Escalate(MathMismatch);

        if (!invoice.ReceiptPresent && invoice.Total > ReceiptRequiredAboveAmount)
            return PolicyGateResult.Escalate(MissingReceipt);

        var vendorKnown = settings.KnownVendors.Contains(invoice.Vendor, StringComparer.OrdinalIgnoreCase);
        if (!vendorKnown)
            return PolicyGateResult.Escalate(UnknownVendor);

        if (invoice.Total > settings.CeilingAmount)
            return PolicyGateResult.Escalate(AutonomyCeiling);

        var categoryAllowed = settings.WhiteListCategories.Contains(invoice.Category, StringComparer.OrdinalIgnoreCase);
        if (!categoryAllowed)
            return PolicyGateResult.Escalate(CategoryNotAllowed);

        return PolicyGateResult.PassToAgent();
    }

    // Normalized so "Bistro 19" / "bistro 19" and 42m / 42.00m (same value, different
    // decimal.ToString() output) don't slip past the duplicate check as distinct keys.
    public static string BuildDedupeKey(InvoiceSubmission invoice) =>
        $"{invoice.Vendor.Trim().ToLowerInvariant()}_{invoice.InvoiceNumber.Trim().ToLowerInvariant()}_{invoice.Total.ToString("F2", CultureInfo.InvariantCulture)}";
}
