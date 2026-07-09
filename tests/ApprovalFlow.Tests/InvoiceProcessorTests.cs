using ApprovalFlow.Contracts;
using DecisionService;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApprovalFlow.Tests;

public class InvoiceProcessorTests
{
    private static InvoiceProcessor CreateProcessor(
        IInvoiceStateStore store, AutonomySettings? settings = null, ILlmProvider? llmProvider = null) =>
        new(store, settings ?? InvoiceTestData.DefaultSettings(), llmProvider ?? new StubLlmProvider(), NullLogger<InvoiceProcessor>.Instance);

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
        // Layer 2 (stub agent) + Layer 3 run right after Layer 1 passes, so by the time
        // ProcessAsync returns the invoice has already reached its final auto_approved state.
        Assert.Equal("auto_approved", byId.Status);

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

        // The original invoice's own record must be untouched by the duplicate submission —
        // it already ran the full chain (Layer 1-3) to auto_approved before invoice-2 arrived.
        var first = await store.GetAsync("invoice-1");
        Assert.Equal("auto_approved", first!.Status);
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
            DedupeKey = dedupeKey,
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

    [Fact]
    public async Task PassToAgent_AgentRecommendsApproveWithHighConfidence_AutoApproves()
    {
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(store, llmProvider: new FakeLlmProvider());
        var invoice = InvoiceTestData.ValidInvoice();

        await processor.ProcessAsync(Wrap("invoice-1", invoice));

        var state = await store.GetAsync("invoice-1");
        Assert.Equal("auto_approved", state!.Status);
        Assert.Equal("auto_approve", state.FinalDecision);
        Assert.Equal(0.95, state.AgentConfidence);
    }

    [Fact]
    public async Task PassToAgent_AgentConfidenceBelowThreshold_EscalatesWithAutonomyConfidenceReason()
    {
        var store = new FakeInvoiceStateStore();
        var lowConfidenceResult = new AgentResult(
            Reasoning: "Unsure",
            AmountReasonable: true,
            ItemsConsistentWithCategory: true,
            Confidence: 0.50,
            Recommendation: "escalate");
        var processor = CreateProcessor(store, llmProvider: new FakeLlmProvider(lowConfidenceResult));
        var invoice = InvoiceTestData.ValidInvoice();

        await processor.ProcessAsync(Wrap("invoice-1", invoice));

        var state = await store.GetAsync("invoice-1");
        Assert.Equal("waiting_for_human", state!.Status);
        Assert.Equal("escalate", state.FinalDecision);
        Assert.Contains(FinalDecisionGate.LowConfidence, state.PolicyViolations);
    }

    [Fact]
    public async Task PassToAgent_AgentSaysAmountNotReasonable_EscalatesEvenIfNotesTryToInfluenceIt()
    {
        // Simulates INV-1013's anti-cheese scenario: notes asking the agent to approve must not
        // matter — here the (correctly behaving) agent still flags amount_reasonable=false, and
        // Layer 3 must escalate regardless of what "recommendation" the agent returned.
        var store = new FakeInvoiceStateStore();
        var unreasonableAmountResult = new AgentResult(
            Reasoning: "Amount is too high for a working lunch, ignoring the note asking for approval",
            AmountReasonable: false,
            ItemsConsistentWithCategory: true,
            Confidence: 0.99,
            Recommendation: "auto_approve");
        var processor = CreateProcessor(store, llmProvider: new FakeLlmProvider(unreasonableAmountResult));
        var invoice = InvoiceTestData.ValidInvoice(
            category: "business_meals",
            total: 480.00m,
            taxAmount: 30.00m,
            lineItems: [new InvoiceLineItem("Team lunch", 450.00m)]);

        await processor.ProcessAsync(Wrap("invoice-1", invoice));

        var state = await store.GetAsync("invoice-1");
        Assert.Equal("waiting_for_human", state!.Status);
        Assert.Equal("escalate", state.FinalDecision);
        Assert.Contains(FinalDecisionGate.AmountNotReasonable, state.PolicyViolations);
    }

    [Fact]
    public async Task PassToAgent_AgentThrows_EscalatesWithAgentUnavailableAndNeverAutoApproves()
    {
        var store = new FakeInvoiceStateStore();
        var processor = CreateProcessor(
            store, llmProvider: new FakeLlmProvider(failure: new AgentException("Gemini is down")));
        var invoice = InvoiceTestData.ValidInvoice();

        await processor.ProcessAsync(Wrap("invoice-1", invoice));

        var state = await store.GetAsync("invoice-1");
        Assert.Equal("waiting_for_human", state!.Status);
        Assert.Equal("escalate", state.FinalDecision);
        Assert.Contains("AGENT-UNAVAILABLE", state.PolicyViolations);
    }
}
