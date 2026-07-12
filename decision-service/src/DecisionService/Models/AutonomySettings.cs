namespace DecisionService;

public class AutonomySettings
{
    public decimal CeilingAmount { get; set; }
    public double ConfidenceThreshold { get; set; }

    // Bound from config key "WhiteListCategories" (env var Autonomy__WhiteListCategories) as a
    // single comma-separated string — .NET's config binder can't turn one env var into a List<string>
    // (it needs indexed keys like :0, :1), so this is hot-configurable with no rebuild required.
    [ConfigurationKeyName("WhiteListCategories")]
    public string WhiteListCategoriesRaw { get; set; } = "";

    [ConfigurationKeyName("KnownVendors")]
    public string KnownVendorsRaw { get; set; } = "";

    public IEnumerable<string> WhiteListCategories =>
        WhiteListCategoriesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IEnumerable<string> KnownVendors =>
        KnownVendorsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
