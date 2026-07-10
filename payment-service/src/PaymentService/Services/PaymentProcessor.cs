using ApprovalFlow.Contracts;

namespace PaymentService;

// Choreographed saga: reserve -> execute -> succeed or compensate (release + fail).
// Pure orchestration logic, no Dapr I/O — testable via fakes, same shape as DecisionService's InvoiceProcessor.
public class PaymentProcessor(
    IPaymentStateStore stateStore,
    IEventPublisher eventPublisher,
    ILogger<PaymentProcessor> logger)
{
    // A specific fixture invoice number the demo journeys force to fail, so Journey D
    // (payment failure + compensation) is reproducible without any manual toggling.
    // FORCE_PAYMENT_FAIL is a general escape hatch for testing other invoices the same way.
    private const string ForcedFailureInvoiceNumber = "INV-1012";

    public async Task ProcessAsync(InvoiceState invoice)
    {
        var invoiceId = invoice.InvoiceId;

        // Dapr pub/sub is at-least-once: invoice.approved can be redelivered. Any existing
        // reservation means this invoice already went through the saga once — same guard
        // style as InvoiceProcessor.ProcessAsync's redelivery check in DecisionService.
        var existing = await stateStore.GetAsync(invoiceId);
        if (existing is not null)
        {
            logger.LogInformation(
                "Invoice {InvoiceId} already has a payment reservation (status={Status}) — ignoring redelivered invoice.approved",
                invoiceId, existing.Status);
            return;
        }

        var reservation = new PaymentReservation
        {
            InvoiceId = invoiceId,
            Status = "reserved",
            Amount = invoice.Total,
            ReservedAt = DateTime.UtcNow
        };
        await stateStore.SaveAsync(invoiceId, reservation);
        logger.LogInformation("Reserved budget for {InvoiceId}: {Amount}", invoiceId, reservation.Amount);

        var succeeded = ExecuteMockPayment(invoice);

        if (succeeded)
        {
            reservation.Status = "paid";
            reservation.PaidAt = DateTime.UtcNow;
            await stateStore.SaveAsync(invoiceId, reservation);

            invoice.PaymentStatus = "paid";
            invoice.PaidAt = reservation.PaidAt;
            await eventPublisher.PublishAsync("payment.succeeded", invoice);

            logger.LogInformation("Payment succeeded for {InvoiceId}", invoiceId);
        }
        else
        {
            // Compensation: release the reservation rather than leaving it stuck as "reserved".
            reservation.Status = "payment-failed";
            await stateStore.SaveAsync(invoiceId, reservation);

            invoice.PaymentStatus = "payment-failed";
            await eventPublisher.PublishAsync("payment.failed", invoice);

            logger.LogWarning("Payment failed for {InvoiceId} — reservation released", invoiceId);
        }
    }

    private static bool ExecuteMockPayment(InvoiceState invoice) => !ShouldForceFailure(invoice);

    private static bool ShouldForceFailure(InvoiceState invoice) =>
        invoice.InvoiceNumber == ForcedFailureInvoiceNumber ||
        Environment.GetEnvironmentVariable("FORCE_PAYMENT_FAIL") == "true";
}
