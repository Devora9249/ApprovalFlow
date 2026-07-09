using ApprovalFlow.Contracts;

namespace DecisionService;

public class InvoiceProcessor(
    IInvoiceStateStore stateStore,
    AutonomySettings settings,
    ILlmProvider llmProvider,
    ILogger<InvoiceProcessor> logger)
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
            await RunAgentAndFinalGateAsync(invoiceId, invoice, state, dedupeKey);
        }
    }

    private async Task RunAgentAndFinalGateAsync(string invoiceId, InvoiceSubmission invoice, InvoiceState state, string dedupeKey)
    {
        var agentRequest = new InvoiceEvaluationRequest(
            invoice.Vendor,
            invoice.Category,
            invoice.Total,
            invoice.Description,
            invoice.LineItems);

        AgentResult agentResult;
        try
        {
            agentResult = await llmProvider.EvaluateInvoiceAsync(agentRequest);
        }
        catch (AgentException ex)
        {
            // Fail fast (M15): if the agent is unreachable or unusable, escalate — never auto-approve.
            logger.LogError(ex, "Agent evaluation failed for {InvoiceId} — escalating", invoiceId);

            state.PolicyViolations.Add("AGENT-UNAVAILABLE");
            state.FinalDecision = "escalate";
            state.Status = "waiting_for_human";
            await stateStore.SaveAsync(invoiceId, state);
            await stateStore.SaveAsync(dedupeKey, state);
            return;
        }

        state.AgentRecommendation = agentResult.Recommendation;
        state.AgentReasoning = agentResult.Reasoning;
        state.AgentConfidence = agentResult.Confidence;
        state.AgentAmountReasonable = agentResult.AmountReasonable;
        state.AgentItemsConsistentWithCategory = agentResult.ItemsConsistentWithCategory;

        var finalResult = FinalDecisionGate.Evaluate(agentResult, settings);

        if (finalResult.Outcome == FinalGateOutcome.Escalate)
        {
            state.PolicyViolations.AddRange(finalResult.Reasons);
            state.FinalDecision = "escalate";
            state.Status = "waiting_for_human";
        }
        else
        {
            state.FinalDecision = "auto_approve";
            state.Status = "auto_approved";
            state.DecidedAt = DateTime.UtcNow;
        }

        await stateStore.SaveAsync(invoiceId, state);
        await stateStore.SaveAsync(dedupeKey, state);

        logger.LogInformation(
            "Layer 2/3 result for {InvoiceId}: agentRecommendation={AgentRecommendation}, confidence={Confidence} | finalDecision={FinalDecision}",
            invoiceId, agentResult.Recommendation, agentResult.Confidence, state.FinalDecision);
    }
}
