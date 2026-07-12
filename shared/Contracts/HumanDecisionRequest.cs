namespace ApprovalFlow.Contracts;

public record HumanDecisionRequest(string Action, string ApproverId, string? Comment = null);
