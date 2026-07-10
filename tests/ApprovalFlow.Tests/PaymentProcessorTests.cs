using ApprovalFlow.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentService;
using Xunit;

namespace ApprovalFlow.Tests;

public class PaymentProcessorTests
{
    private static PaymentProcessor CreateProcessor(FakePaymentStateStore store, FakePaymentEventPublisher eventPublisher) =>
        new(store, eventPublisher, NullLogger<PaymentProcessor>.Instance);

    private static InvoiceState ApprovedInvoice(string invoiceId = "invoice-1", string invoiceNumber = "INV-1001", decimal total = 120.00m) =>
        new()
        {
            InvoiceId = invoiceId,
            CorrelationId = invoiceId,
            Submitter = "dana.cohen@clearspend.example",
            Vendor = "Staples",
            InvoiceNumber = invoiceNumber,
            Category = "office_supplies",
            Total = total,
            DedupeKey = $"staples_{invoiceNumber}_{total:F2}",
            Status = "auto_approved"
        };

    [Fact]
    public async Task HappyPath_ReservesThenPays_PublishesPaymentSucceeded()
    {
        var store = new FakePaymentStateStore();
        var eventPublisher = new FakePaymentEventPublisher();
        var processor = CreateProcessor(store, eventPublisher);
        var invoice = ApprovedInvoice();

        await processor.ProcessAsync(invoice);

        var reservation = await store.GetAsync(invoice.InvoiceId);
        Assert.Equal("paid", reservation!.Status);
        Assert.NotNull(reservation.PaidAt);

        var published = Assert.Single(eventPublisher.Published);
        Assert.Equal("payment.succeeded", published.Topic);
        Assert.Equal("paid", Assert.IsType<InvoiceState>(published.Payload).PaymentStatus);
    }

    [Fact]
    public async Task ForcedFailureInvoice_CompensatesReservationAndPublishesPaymentFailed()
    {
        var store = new FakePaymentStateStore();
        var eventPublisher = new FakePaymentEventPublisher();
        var processor = CreateProcessor(store, eventPublisher);
        var invoice = ApprovedInvoice(invoiceNumber: "INV-1012", total: 9500.00m);

        await processor.ProcessAsync(invoice);

        var reservation = await store.GetAsync(invoice.InvoiceId);
        // No orphaned reservation: compensation must move it out of "reserved", not leave it stuck.
        Assert.Equal("payment-failed", reservation!.Status);

        var published = Assert.Single(eventPublisher.Published);
        Assert.Equal("payment.failed", published.Topic);
        Assert.Equal("payment-failed", Assert.IsType<InvoiceState>(published.Payload).PaymentStatus);
    }

    [Fact]
    public async Task RedeliveredInvoiceApprovedEvent_SecondDeliveryIsANoOp()
    {
        var store = new FakePaymentStateStore();
        var eventPublisher = new FakePaymentEventPublisher();
        var processor = CreateProcessor(store, eventPublisher);
        var invoice = ApprovedInvoice();

        await processor.ProcessAsync(invoice);
        await processor.ProcessAsync(invoice);

        // Only one payment attempt despite two deliveries of the same invoice.approved event.
        Assert.Single(eventPublisher.Published);
    }
}
