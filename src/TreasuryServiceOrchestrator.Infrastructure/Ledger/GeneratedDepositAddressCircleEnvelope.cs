using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class GeneratedDepositAddressCircleEnvelope
{
    [JsonPropertyName("data")]
    public GeneratedDepositAddressCircleData Data { get; set; } = new();
}
