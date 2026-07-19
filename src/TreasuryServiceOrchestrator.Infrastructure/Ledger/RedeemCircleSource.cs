using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class RedeemCircleSource
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "wallet";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}
