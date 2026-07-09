using System.Text.Json.Serialization;

namespace DecisionService;

public record AgentResult(
    [property: JsonPropertyName("reasoning")] string Reasoning,
    [property: JsonPropertyName("amount_reasonable")] bool AmountReasonable,
    [property: JsonPropertyName("items_consistent_with_category")] bool ItemsConsistentWithCategory,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("recommendation")] string Recommendation);
