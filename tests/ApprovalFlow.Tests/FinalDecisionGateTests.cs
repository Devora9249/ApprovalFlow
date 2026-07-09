using DecisionService;
using Xunit;

namespace ApprovalFlow.Tests;

public class FinalDecisionGateTests
{
    private static AgentResult ValidAgentResult(
        bool amountReasonable = true,
        bool itemsConsistent = true,
        double confidence = 0.95) =>
        new(
            Reasoning: "Looks fine",
            AmountReasonable: amountReasonable,
            ItemsConsistentWithCategory: itemsConsistent,
            Confidence: confidence,
            Recommendation: "auto_approve");

    [Fact]
    public void AllChecksPass_ReturnsAutoApprove()
    {
        var result = FinalDecisionGate.Evaluate(ValidAgentResult(), InvoiceTestData.DefaultSettings());

        Assert.Equal(FinalGateOutcome.AutoApprove, result.Outcome);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void ConfidenceBelowThreshold_EscalatesAutonomyConfidence()
    {
        var result = FinalDecisionGate.Evaluate(ValidAgentResult(confidence: 0.79), InvoiceTestData.DefaultSettings());

        Assert.Equal(FinalGateOutcome.Escalate, result.Outcome);
        Assert.Equal([FinalDecisionGate.LowConfidence], result.Reasons);
    }

    [Fact]
    public void ConfidenceExactlyAtThreshold_DoesNotEscalateOnConfidenceCheck()
    {
        var result = FinalDecisionGate.Evaluate(ValidAgentResult(confidence: 0.80), InvoiceTestData.DefaultSettings());

        Assert.Equal(FinalGateOutcome.AutoApprove, result.Outcome);
    }

    [Fact]
    public void AmountNotReasonable_EscalatesAmountNotReasonable()
    {
        var result = FinalDecisionGate.Evaluate(ValidAgentResult(amountReasonable: false), InvoiceTestData.DefaultSettings());

        Assert.Equal(FinalGateOutcome.Escalate, result.Outcome);
        Assert.Equal([FinalDecisionGate.AmountNotReasonable], result.Reasons);
    }

    [Fact]
    public void ItemsNotConsistentWithCategory_EscalatesItemsInconsistent()
    {
        var result = FinalDecisionGate.Evaluate(ValidAgentResult(itemsConsistent: false), InvoiceTestData.DefaultSettings());

        Assert.Equal(FinalGateOutcome.Escalate, result.Outcome);
        Assert.Equal([FinalDecisionGate.ItemsInconsistent], result.Reasons);
    }

    [Fact]
    public void MultipleViolations_AmountAndItemsBothReported()
    {
        // Confidence passes here (0.95, default) — isolates that amount + items accumulate together,
        // rather than the first failing check suppressing the second.
        var result = FinalDecisionGate.Evaluate(
            ValidAgentResult(amountReasonable: false, itemsConsistent: false),
            InvoiceTestData.DefaultSettings());

        Assert.Equal(FinalGateOutcome.Escalate, result.Outcome);
        Assert.Equal([FinalDecisionGate.AmountNotReasonable, FinalDecisionGate.ItemsInconsistent], result.Reasons);
    }

    [Fact]
    public void MultipleViolations_AllThreeReported_WhenConfidenceAmountAndItemsAllFail()
    {
        var result = FinalDecisionGate.Evaluate(
            ValidAgentResult(amountReasonable: false, itemsConsistent: false, confidence: 0.50),
            InvoiceTestData.DefaultSettings());

        Assert.Equal(FinalGateOutcome.Escalate, result.Outcome);
        Assert.Equal(
            [FinalDecisionGate.LowConfidence, FinalDecisionGate.AmountNotReasonable, FinalDecisionGate.ItemsInconsistent],
            result.Reasons);
    }
}
