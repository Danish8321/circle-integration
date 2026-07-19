using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class ListDepositsCircleAmount
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = "0";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;
}
