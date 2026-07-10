using ApprovalFlow.Contracts;

namespace DecisionService;

// Applies the outcome of the payment saga (PaymentService) back onto the invoice's own
// record here. Both state copies (invoiceId + dedupe key) must move together, same as
// HumanDecisionProcessor, or a later duplicate-check read of the dedupe-key copy would
// see stale paymentStatus.
public class PaymentResultProcessor(IInvoiceStateStore stateStore, ILogger<PaymentResultProcessor> logger)
{
    public async Task ApplyAsync(InvoiceState incoming)
    {
        var state = await stateStore.GetAsync(incoming.InvoiceId);
        if (state is null)
        {
            logger.LogWarning("Payment result for unknown invoice {InvoiceId} — ignoring", incoming.InvoiceId);
            return;
        }

        state.PaymentStatus = incoming.PaymentStatus;
        state.PaidAt = incoming.PaidAt;

        await stateStore.SaveAsync(incoming.InvoiceId, state);
        await stateStore.SaveAsync(state.DedupeKey, state);

        logger.LogInformation(
            "Payment result applied for {InvoiceId}: paymentStatus={PaymentStatus}",
            incoming.InvoiceId, state.PaymentStatus);
    }
}
