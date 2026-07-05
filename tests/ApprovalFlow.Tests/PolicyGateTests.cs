using ApprovalFlow.Contracts;
using DecisionService;
using Xunit;

namespace ApprovalFlow.Tests;

public class PolicyGateTests
{
    [Fact]
    public void AllChecksPass_ReturnsPassToAgent()
    {
        var result = PolicyGate.EvaluateDeterministicChecks(InvoiceTestData.ValidInvoice(), InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.PassToAgent, result.Outcome);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void LineItemsPlusTaxNotEqualToTotal_EscalatesGlobalMath()
    {
        var invoice = InvoiceTestData.ValidInvoice(total: 999.00m);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.Escalate, result.Outcome);
        Assert.Equal(PolicyGate.MathMismatch, result.Reason);
    }

    [Fact]
    public void NoReceiptAboveTwentyFiveDollars_EscalatesGlobalReceipt()
    {
        var invoice = InvoiceTestData.ValidInvoice(receiptPresent: false);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.Escalate, result.Outcome);
        Assert.Equal(PolicyGate.MissingReceipt, result.Reason);
    }

    [Fact]
    public void NoReceiptAtOrBelowTwentyFiveDollars_DoesNotEscalateOnReceiptCheck()
    {
        var invoice = InvoiceTestData.ValidInvoice(
            receiptPresent: false,
            total: 25.00m,
            taxAmount: 0m,
            lineItems: [new InvoiceLineItem("Notepad", 25.00m)]);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.PassToAgent, result.Outcome);
    }

    [Fact]
    public void UnknownVendor_EscalatesGlobalVendor()
    {
        var invoice = InvoiceTestData.ValidInvoice(vendor: "Totally Unknown Vendor LLC");

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.Escalate, result.Outcome);
        Assert.Equal(PolicyGate.UnknownVendor, result.Reason);
    }

    [Fact]
    public void KnownVendorMatch_IsCaseInsensitive()
    {
        var invoice = InvoiceTestData.ValidInvoice(vendor: "STAPLES");

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.PassToAgent, result.Outcome);
    }

    [Fact]
    public void TotalAboveCeiling_EscalatesAutonomyCeiling()
    {
        var invoice = InvoiceTestData.ValidInvoice(
            total: 750.00m,
            taxAmount: 0m,
            lineItems: [new InvoiceLineItem("Standing desk", 750.00m)]);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.Escalate, result.Outcome);
        Assert.Equal(PolicyGate.AutonomyCeiling, result.Reason);
    }

    [Fact]
    public void TotalAtExactlyCeiling_DoesNotEscalateOnAmountCheck()
    {
        var invoice = InvoiceTestData.ValidInvoice(
            total: 500.00m,
            taxAmount: 0m,
            lineItems: [new InvoiceLineItem("Monitor", 500.00m)]);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.PassToAgent, result.Outcome);
    }

    [Fact]
    public void CategoryNotInWhiteList_EscalatesCategoryNotAllowed()
    {
        var invoice = InvoiceTestData.ValidInvoice(category: "alcohol");

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGateOutcome.Escalate, result.Outcome);
        Assert.Equal(PolicyGate.CategoryNotAllowed, result.Reason);
    }

    [Fact]
    public void ChecksRunInSpecOrder_MathMismatchWinsOverLaterFailures()
    {
        // Also fails the receipt check and is over ceiling — GLOBAL-MATH must win since it's check #2.
        var invoice = InvoiceTestData.ValidInvoice(
            total: 999.00m,
            receiptPresent: false,
            vendor: "Unknown Co");

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGate.MathMismatch, result.Reason);
    }

    [Fact]
    public void ChecksRunInSpecOrder_ReceiptWinsOverVendorAndCeilingAndCategory()
    {
        var invoice = InvoiceTestData.ValidInvoice(
            receiptPresent: false,
            vendor: "Unknown Co",
            category: "alcohol",
            total: 750.00m,
            taxAmount: 0m,
            lineItems: [new InvoiceLineItem("Mystery item", 750.00m)]);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGate.MissingReceipt, result.Reason);
    }

    [Fact]
    public void ChecksRunInSpecOrder_VendorWinsOverCeilingAndCategory()
    {
        var invoice = InvoiceTestData.ValidInvoice(
            vendor: "Unknown Co",
            category: "alcohol",
            total: 750.00m,
            taxAmount: 0m,
            lineItems: [new InvoiceLineItem("Mystery item", 750.00m)]);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGate.UnknownVendor, result.Reason);
    }

    [Fact]
    public void ChecksRunInSpecOrder_CeilingWinsOverCategory()
    {
        var invoice = InvoiceTestData.ValidInvoice(
            category: "alcohol",
            total: 750.00m,
            taxAmount: 0m,
            lineItems: [new InvoiceLineItem("Mystery item", 750.00m)]);

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, InvoiceTestData.DefaultSettings());

        Assert.Equal(PolicyGate.AutonomyCeiling, result.Reason);
    }
}
