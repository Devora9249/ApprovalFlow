namespace DecisionService;

public class AutonomySettings
{
    public decimal CeilingAmount { get; set; }
    public double ConfidenceThreshold { get; set; }
    public List<string> WhiteListCategories { get; set; } = [];
    public List<string> KnownVendors { get; set; } = [];
}
