using ApprovalFlow.Contracts;
using DecisionService;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApprovalFlow.Tests;

public class InvoiceProcessorTests
{
    private static InvoiceProcessor CreateProcessor(IInvoiceStateStore store, AutonomySettings? settings = null) =>
        new(store, settings ?? InvoiceTestData.DefaultSettings(), NullLogger<InvoiceProcessor>.Instance);

    private static InvoiceSubmittedEvent Wrap(string invoiceId, InvoiceSubmission invoice) =>
        new(invoiceId, invoiceId, DateTime.UtcNow, invoice);

    [Fact]
    public async Task FirstSubmission_PassesLayer1_SavedUnderInvoiceIdAndDedupeKey()
    {
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(store);
        var invoice = InvoiceTestData.ValidInvoice();

        await processor.ProcessAsync(Wrap("invoice-1", invoice));

        var byId = await store.GetAsync("invoice-1");
        Assert.NotNull(byId);
        Assert.Equal("pass_to_agent", byId!.DeterministicResult);
        Assert.Equal("processing", byId.Status);

        var dedupeKey = PolicyGate.BuildDedupeKey(invoice);
        var byDedupeKey = await store.GetAsync(dedupeKey);
        Assert.NotNull(byDedupeKey);
    }

    [Fact]
    public async Task EscalatedInvoice_SetsStatusWaitingForHuman()
    {
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(store);
        var invoice = InvoiceTestData.ValidInvoice(category: "alcohol");

        await processor.ProcessAsync(Wrap("invoice-1", invoice));

        var byId = await store.GetAsync("invoice-1");
        Assert.Equal("waiting_for_human", byId!.Status);
        Assert.Equal("escalate", byId.DeterministicResult);
        Assert.Equal(PolicyGate.CategoryNotAllowed, byId.DeterministicReason);
    }

    [Fact]
    public async Task SecondSubmission_SameVendorInvoiceNumberAndTotal_IsBlockedAsDuplicate()
    {
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(store);
        var invoice = InvoiceTestData.ValidInvoice();

        await processor.ProcessAsync(Wrap("invoice-1", invoice));
        await processor.ProcessAsync(Wrap("invoice-2", invoice));

        var second = await store.GetAsync("invoice-2");
        Assert.Equal("duplicate", second!.Status);
        Assert.Equal("duplicate", second.DeterministicResult);

        // The original invoice's own record must be untouched by the duplicate submission.
        var first = await store.GetAsync("invoice-1");
        Assert.Equal("processing", first!.Status);
    }

    [Fact]
    public async Task Resubmission_AfterWaitingForSubmitter_IsNotBlockedAsDuplicate()
    {
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(store);
        var invoice = InvoiceTestData.ValidInvoice();

        var dedupeKey = PolicyGate.BuildDedupeKey(invoice);
        await store.SaveAsync(dedupeKey, new InvoiceState
        {
            InvoiceId = "invoice-1",
            CorrelationId = "invoice-1",
            Submitter = invoice.Submitter,
            Vendor = invoice.Vendor,
            Category = invoice.Category,
            Total = invoice.Total,
            SubmittedAt = DateTime.UtcNow,
            Status = "waiting_for_submitter"
        });

        await processor.ProcessAsync(Wrap("invoice-2", invoice));

        var second = await store.GetAsync("invoice-2");
        Assert.NotEqual("duplicate", second!.Status);
        Assert.Equal("pass_to_agent", second.DeterministicResult);
    }

    [Fact]
    public async Task RedeliveredEvent_SameInvoiceIdProcessedTwice_SecondDeliveryIsANoOp()
    {
        // Dapr pub/sub is at-least-once — the same invoiceId can arrive twice.
        // The retry must not overwrite the first (correct) result with "duplicate".
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(store);
        var invoice = InvoiceTestData.ValidInvoice();
        var evt = Wrap("invoice-1", invoice);

        await processor.ProcessAsync(evt);
        await processor.ProcessAsync(evt);

        var byId = await store.GetAsync("invoice-1");
        Assert.Equal("pass_to_agent", byId!.DeterministicResult);
        Assert.NotEqual("duplicate", byId.Status);
    }

    [Fact]
    public async Task DifferentTotal_SameVendorAndInvoiceNumber_IsNotADuplicate()
    {
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(store);
        var first = InvoiceTestData.ValidInvoice(total: 120.00m);
        var second = InvoiceTestData.ValidInvoice(
            total: 150.00m,
            taxAmount: 20.00m,
            lineItems: [new InvoiceLineItem("Paper + pens", 130.00m)]);

        await processor.ProcessAsync(Wrap("invoice-1", first));
        await processor.ProcessAsync(Wrap("invoice-2", second));

        var secondState = await store.GetAsync("invoice-2");
        Assert.NotEqual("duplicate", secondState!.Status);
    }
}
