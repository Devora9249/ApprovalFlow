using ApprovalFlow.Contracts;

namespace DecisionService;

public class InvoiceProcessor(IInvoiceStateStore stateStore, AutonomySettings settings, ILogger<InvoiceProcessor> logger)
{
    public async Task ProcessAsync(InvoiceSubmittedEvent submitted)
    {
        var invoiceId = submitted.InvoiceId;
        var invoice = submitted.Invoice;

        // Dapr pub/sub is at-least-once, not exactly-once: the same invoiceId can be
        // redelivered (e.g. if the subscriber didn't ack in time). Without this guard,
        // reprocessing a redelivered event finds its own prior result under the dedupe
        // key and wrongly flags the invoice as a duplicate of itself.
        var alreadyProcessed = await stateStore.GetAsync(invoiceId);
        if (alreadyProcessed is not null)
        {
            logger.LogInformation(
                "Invoice {InvoiceId} was already processed (status={Status}) — ignoring redelivered event",
                invoiceId, alreadyProcessed.Status);
            return;
        }

        var state = new InvoiceState
        {
            InvoiceId = invoiceId,
            CorrelationId = submitted.CorrelationId,
            Submitter = invoice.Submitter,
            Vendor = invoice.Vendor,
            Category = invoice.Category,
            Total = invoice.Total,
            SubmittedAt = submitted.SubmittedAt,
            Status = "processing"
        };
        await stateStore.SaveAsync(invoiceId, state);

        // Dedupe key is a secondary index: the authoritative state for a given
        // vendor+invoiceNumber+total triplet lives here too (not just under invoiceId),
        // so the duplicate check can look it up without a full state store query.
        // NOTE: when the HITL "request_more_info" endpoint (Day 6+) sets an invoice's
        // own status to waiting_for_submitter, it must also update this dedupe-key copy,
        // or a legitimate resubmission will be wrongly flagged as a duplicate.
        var dedupeKey = PolicyGate.BuildDedupeKey(invoice);
        var existing = await stateStore.GetAsync(dedupeKey);

        if (existing is not null && existing.Status != "waiting_for_submitter")
        {
            state.DeterministicResult = "duplicate";
            state.DeterministicReason = "Matching vendor, invoice number and total already processed";
            state.Status = "duplicate";
            await stateStore.SaveAsync(invoiceId, state);

            logger.LogInformation("PolicyGate result for {InvoiceId}: duplicate | Reason: {Reason}",
                invoiceId, state.DeterministicReason);
            return;
        }

        var result = PolicyGate.EvaluateDeterministicChecks(invoice, settings);

        state.DeterministicResult = result.Outcome == PolicyGateOutcome.Escalate ? "escalate" : "pass_to_agent";
        state.DeterministicReason = result.Outcome == PolicyGateOutcome.Escalate ? result.Reason : "passed all checks";
        state.Status = result.Outcome == PolicyGateOutcome.Escalate ? "waiting_for_human" : "processing";

        await stateStore.SaveAsync(invoiceId, state);
        await stateStore.SaveAsync(dedupeKey, state);

        logger.LogInformation("PolicyGate result for {InvoiceId}: {Result} | Reason: {Reason}",
            invoiceId, state.DeterministicResult, state.DeterministicReason ?? "n/a");

        if (result.Outcome == PolicyGateOutcome.PassToAgent)
        {
            logger.LogInformation("Invoice {InvoiceId} passed Layer 1 — awaiting Layer 2 (AI agent, not yet implemented)",
                invoiceId);
        }
    }
}
