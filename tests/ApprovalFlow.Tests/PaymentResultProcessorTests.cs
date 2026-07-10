using ApprovalFlow.Contracts;
using DecisionService;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApprovalFlow.Tests;

public class PaymentResultProcessorTests
{
    private static InvoiceState SavedInvoice(string invoiceId = "invoice-1", string dedupeKey = "staples_inv-1001_120.00") =>
        new()
        {
            InvoiceId = invoiceId,
            CorrelationId = invoiceId,
            Submitter = "dana.cohen@clearspend.example",
            Vendor = "Staples",
            InvoiceNumber = "INV-1001",
            Category = "office_supplies",
            Total = 120.00m,
            DedupeKey = dedupeKey,
            Status = "auto_approved"
        };

    [Fact]
    public async Task PaymentSucceeded_UpdatesBothStateCopies()
    {
        var store = new FakeInvoiceStateStore();
        var saved = SavedInvoice();
        await store.SaveAsync(saved.InvoiceId, saved);
        await store.SaveAsync(saved.DedupeKey, saved);

        var processor = new PaymentResultProcessor(store, NullLogger<PaymentResultProcessor>.Instance);
        var paidAt = DateTime.UtcNow;
        var incoming = new InvoiceState
        {
            InvoiceId = saved.InvoiceId,
            CorrelationId = saved.CorrelationId,
            Submitter = saved.Submitter,
            Vendor = saved.Vendor,
            InvoiceNumber = saved.InvoiceNumber,
            Category = saved.Category,
            Total = saved.Total,
            DedupeKey = saved.DedupeKey,
            Status = saved.Status,
            PaymentStatus = "paid",
            PaidAt = paidAt
        };

        await processor.ApplyAsync(incoming);

        var byId = await store.GetAsync(saved.InvoiceId);
        var byDedupeKey = await store.GetAsync(saved.DedupeKey);
        Assert.Equal("paid", byId!.PaymentStatus);
        Assert.Equal(paidAt, byId.PaidAt);
        Assert.Equal("paid", byDedupeKey!.PaymentStatus);
    }

    [Fact]
    public async Task PaymentFailed_SetsPaymentStatusWithoutPaidAt()
    {
        var store = new FakeInvoiceStateStore();
        var saved = SavedInvoice();
        await store.SaveAsync(saved.InvoiceId, saved);
        await store.SaveAsync(saved.DedupeKey, saved);

        var processor = new PaymentResultProcessor(store, NullLogger<PaymentResultProcessor>.Instance);
        var incoming = new InvoiceState
        {
            InvoiceId = saved.InvoiceId,
            CorrelationId = saved.CorrelationId,
            Submitter = saved.Submitter,
            Vendor = saved.Vendor,
            InvoiceNumber = saved.InvoiceNumber,
            Category = saved.Category,
            Total = saved.Total,
            DedupeKey = saved.DedupeKey,
            Status = saved.Status,
            PaymentStatus = "payment-failed"
        };

        await processor.ApplyAsync(incoming);

        var byId = await store.GetAsync(saved.InvoiceId);
        Assert.Equal("payment-failed", byId!.PaymentStatus);
        Assert.Null(byId.PaidAt);
    }
}
