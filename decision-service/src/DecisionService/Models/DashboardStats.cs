namespace DecisionService;

public record DashboardStats(
    int TotalInvoices,
    int AutoApproved,
    int HumanApproved,
    int Escalated,
    int Rejected,
    int Duplicates,
    decimal TotalAutoApprovedAmount,
    decimal TotalHumanApprovedAmount);
