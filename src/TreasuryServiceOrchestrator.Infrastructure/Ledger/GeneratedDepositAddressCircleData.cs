using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class GeneratedDepositAddressCircleData
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("chain")]
    public string Chain { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;
}
