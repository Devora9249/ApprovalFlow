using ApprovalFlow.Contracts;

namespace DecisionService;

public enum HumanDecisionOutcome
{
    Approved,
    Rejected,
    RequestedMoreInfo,
    InvoiceNotFound,
    NotAwaitingHumanDecision,
    UnknownAction
}

public record HumanDecisionResult(HumanDecisionOutcome Outcome, InvoiceState? State);

public class HumanDecisionProcessor(
    IInvoiceStateStore stateStore,
    IEventPublisher eventPublisher,
    ILogger<HumanDecisionProcessor> logger)
{
    public async Task<HumanDecisionResult> ApplyAsync(string invoiceId, HumanDecisionRequest request)
    {
        var state = await stateStore.GetAsync(invoiceId);
        if (state is null)
        {
            logger.LogWarning("Decision rejected: invoice {InvoiceId} not found", invoiceId);
            return new HumanDecisionResult(HumanDecisionOutcome.InvoiceNotFound, null);
        }

        if (state.Status != "waiting_for_human")
        {
            logger.LogWarning(
                "Decision rejected for {InvoiceId}: status is {Status}, not waiting_for_human",
                invoiceId, state.Status);
            return new HumanDecisionResult(HumanDecisionOutcome.NotAwaitingHumanDecision, state);
        }

        var (newStatus, outcome) = request.Action switch
        {
            "approve" => ("approved", HumanDecisionOutcome.Approved),
            "reject" => ("rejected", HumanDecisionOutcome.Rejected),
            "request_more_info" => ("waiting_for_submitter", HumanDecisionOutcome.RequestedMoreInfo),
            _ => ((string?)null, HumanDecisionOutcome.UnknownAction)
        };

        if (newStatus is null)
        {
            logger.LogWarning("Decision rejected for {InvoiceId}: unknown action {Action}", invoiceId, request.Action);
            return new HumanDecisionResult(HumanDecisionOutcome.UnknownAction, state);
        }

        state.Status = newStatus;
        state.DecidedBy = request.ApproverId;
        state.HumanAction = request.Action;
        state.DecidedAt = DateTime.UtcNow;
        state.Comment = request.Comment;

        // Both copies must move together — the dedupe-key copy is what the duplicate check in
        // InvoiceProcessor reads, so if only the invoiceId copy were updated, a resubmission
        // after request_more_info would still see the old "waiting_for_human"/"escalate" state
        // there and be wrongly blocked as a duplicate.
        await stateStore.SaveAsync(invoiceId, state);
        await stateStore.SaveAsync(state.DedupeKey, state);
        await stateStore.RemovePendingIdAsync(invoiceId);

        logger.LogInformation(
            "Human decision for {InvoiceId}: action={Action}, approverId={ApproverId} -> status={Status}",
            invoiceId, request.Action, request.ApproverId, state.Status);

        if (outcome == HumanDecisionOutcome.Approved)
        {
            await eventPublisher.PublishAsync("invoice.approved", state);
            logger.LogInformation("Published invoice.approved for {InvoiceId}", invoiceId);
        }

        return new HumanDecisionResult(outcome, state);
    }
}
