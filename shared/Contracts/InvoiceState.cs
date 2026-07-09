namespace ApprovalFlow.Contracts;

public class InvoiceState
{
    public required string InvoiceId { get; set; }
    public required string CorrelationId { get; set; }
    public required string Submitter { get; set; }
    public required string Vendor { get; set; }
    public required string Category { get; set; }
    public decimal Total { get; set; }
    public DateTime SubmittedAt { get; set; }

    // Secondary-index key (vendor+invoiceNumber+total) this record is also stored under —
    // see PolicyGate.BuildDedupeKey. Persisted here so later steps (HITL decision) can find
    // the dedupe-key copy without re-deriving it from a raw InvoiceSubmission.
    public required string DedupeKey { get; set; }

    // Layer 1 result
    public string? DeterministicResult { get; set; }
    public string? DeterministicReason { get; set; }

    // Layer 2 result
    public string? AgentRecommendation { get; set; }
    public string? AgentReasoning { get; set; }
    public double? AgentConfidence { get; set; }
    public bool? AgentAmountReasonable { get; set; }
    public bool? AgentItemsConsistentWithCategory { get; set; }
    public List<string> PolicyViolations { get; set; } = [];
    public DateTime? EscalatedAt { get; set; }

    // Final decision
    public required string Status { get; set; }
    public string? FinalDecision { get; set; }
    public DateTime? DecidedAt { get; set; }

    // Human decision
    public string? DecidedBy { get; set; }
    public string? HumanAction { get; set; }
    public string? Comment { get; set; }

    // Payment
    public string? PaymentStatus { get; set; }
    public DateTime? PaidAt { get; set; }
}
