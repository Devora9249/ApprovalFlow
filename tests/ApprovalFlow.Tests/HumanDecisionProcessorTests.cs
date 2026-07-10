using ApprovalFlow.Contracts;
using DecisionService;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApprovalFlow.Tests;

public class HumanDecisionProcessorTests
{
    private static InvoiceProcessor CreateInvoiceProcessor(IInvoiceStateStore store) =>
        new(store, InvoiceTestData.DefaultSettings(), new StubLlmProvider(), new FakeEventPublisher(), NullLogger<InvoiceProcessor>.Instance);

    private static HumanDecisionProcessor CreateDecisionProcessor(IInvoiceStateStore store, IEventPublisher publisher) =>
        new(store, publisher, NullLogger<HumanDecisionProcessor>.Instance);

    // Escalates via Layer 1 (category not on the white list) so the invoice lands in
    // waiting_for_human without needing to go through the agent.
    private static async Task<InvoiceSubmission> EscalateInvoiceAsync(
        IInvoiceStateStore store, string invoiceId, InvoiceSubmission? invoice = null)
    {
        invoice ??= InvoiceTestData.ValidInvoice(category: "alcohol");
        var evt = new InvoiceSubmittedEvent(invoiceId, invoiceId, DateTime.UtcNow, invoice);
        await CreateInvoiceProcessor(store).ProcessAsync(evt);
        return invoice;
    }

    [Fact]
    public async Task Approve_SetsStatusApproved_PublishesInvoiceApproved_RemovesFromPendingQueue()
    {
        var store = new FakeInvoiceStateStore();
        var publisher = new FakeEventPublisher();
        var invoice = await EscalateInvoiceAsync(store, "invoice-1");
        var decisionProcessor = CreateDecisionProcessor(store, publisher);

        var result = await decisionProcessor.ApplyAsync("invoice-1", new HumanDecisionRequest("approve", "manager1"));

        Assert.Equal(HumanDecisionOutcome.Approved, result.Outcome);
        Assert.Equal("approved", result.State!.Status);
        Assert.Equal("manager1", result.State.DecidedBy);
        Assert.Equal("approve", result.State.HumanAction);
        Assert.NotNull(result.State.DecidedAt);

        var single = Assert.Single(publisher.Published);
        Assert.Equal("invoice.approved", single.Topic);
        Assert.Equal("approved", Assert.IsType<InvoiceState>(single.Payload).Status);

        Assert.DoesNotContain("invoice-1", await store.GetPendingIdsAsync());

        var dedupeKey = PolicyGate.BuildDedupeKey(invoice);
        var byDedupeKey = await store.GetAsync(dedupeKey);
        Assert.Equal("approved", byDedupeKey!.Status);
    }

    [Fact]
    public async Task Reject_SetsStatusRejected_NoEventPublished()
    {
        var store = new FakeInvoiceStateStore();
        var publisher = new FakeEventPublisher();
        await EscalateInvoiceAsync(store, "invoice-1");
        var decisionProcessor = CreateDecisionProcessor(store, publisher);

        var result = await decisionProcessor.ApplyAsync(
            "invoice-1", new HumanDecisionRequest("reject", "manager1", "Not a valid business expense"));

        Assert.Equal(HumanDecisionOutcome.Rejected, result.Outcome);
        Assert.Equal("rejected", result.State!.Status);
        Assert.Equal("Not a valid business expense", result.State.Comment);
        Assert.Empty(publisher.Published);
        Assert.DoesNotContain("invoice-1", await store.GetPendingIdsAsync());
    }

    [Fact]
    public async Task RequestMoreInfo_SetsStatusWaitingForSubmitter_UpdatesBothKeys()
    {
        var store = new FakeInvoiceStateStore();
        var publisher = new FakeEventPublisher();
        var invoice = await EscalateInvoiceAsync(store, "invoice-1");
        var decisionProcessor = CreateDecisionProcessor(store, publisher);

        var result = await decisionProcessor.ApplyAsync(
            "invoice-1", new HumanDecisionRequest("request_more_info", "manager1", "Please attach a receipt"));

        Assert.Equal(HumanDecisionOutcome.RequestedMoreInfo, result.Outcome);
        Assert.Equal("waiting_for_submitter", result.State!.Status);

        var dedupeKey = PolicyGate.BuildDedupeKey(invoice);
        var byDedupeKey = await store.GetAsync(dedupeKey);
        Assert.Equal("waiting_for_submitter", byDedupeKey!.Status);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Resubmission_AfterRequestMoreInfo_IsNotBlockedAsDuplicate()
    {
        var store = new FakeInvoiceStateStore();
        var publisher = new FakeEventPublisher();
        var invoice = InvoiceTestData.ValidInvoice(category: "alcohol");
        await EscalateInvoiceAsync(store, "invoice-1", invoice);
        var decisionProcessor = CreateDecisionProcessor(store, publisher);
        await decisionProcessor.ApplyAsync("invoice-1", new HumanDecisionRequest("request_more_info", "manager1"));

        var resubmitted = invoice with { Category = "business_meals" };
        var invoiceProcessor = CreateInvoiceProcessor(store);
        await invoiceProcessor.ProcessAsync(new InvoiceSubmittedEvent("invoice-2", "invoice-2", DateTime.UtcNow, resubmitted));

        var second = await store.GetAsync("invoice-2");
        Assert.NotEqual("duplicate", second!.Status);
    }

    [Fact]
    public async Task DecisionOnUnknownInvoice_ReturnsInvoiceNotFound()
    {
        var store = new FakeInvoiceStateStore();
        var decisionProcessor = CreateDecisionProcessor(store, new FakeEventPublisher());

        var result = await decisionProcessor.ApplyAsync("missing-invoice", new HumanDecisionRequest("approve", "manager1"));

        Assert.Equal(HumanDecisionOutcome.InvoiceNotFound, result.Outcome);
        Assert.Null(result.State);
    }

    [Fact]
    public async Task DecisionOnInvoiceNotWaitingForHuman_ReturnsNotAwaitingHumanDecision()
    {
        var store = new FakeInvoiceStateStore();
        var invoice = InvoiceTestData.ValidInvoice(); // clean pass -> auto_approved, never reaches the human queue
        await CreateInvoiceProcessor(store).ProcessAsync(
            new InvoiceSubmittedEvent("invoice-1", "invoice-1", DateTime.UtcNow, invoice));
        var decisionProcessor = CreateDecisionProcessor(store, new FakeEventPublisher());

        var result = await decisionProcessor.ApplyAsync("invoice-1", new HumanDecisionRequest("approve", "manager1"));

        Assert.Equal(HumanDecisionOutcome.NotAwaitingHumanDecision, result.Outcome);
        Assert.Equal("auto_approved", result.State!.Status);
    }

    [Fact]
    public async Task UnknownAction_ReturnsUnknownActionOutcome_LeavesStateUntouched()
    {
        var store = new FakeInvoiceStateStore();
        await EscalateInvoiceAsync(store, "invoice-1");
        var decisionProcessor = CreateDecisionProcessor(store, new FakeEventPublisher());

        var result = await decisionProcessor.ApplyAsync("invoice-1", new HumanDecisionRequest("do_something_else", "manager1"));

        Assert.Equal(HumanDecisionOutcome.UnknownAction, result.Outcome);
        Assert.Equal("waiting_for_human", (await store.GetAsync("invoice-1"))!.Status);
    }
}
