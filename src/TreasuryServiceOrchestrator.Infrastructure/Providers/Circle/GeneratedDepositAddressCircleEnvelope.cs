using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class GeneratedDepositAddressCircleEnvelope
{
    [JsonPropertyName("data")]
    public GeneratedDepositAddressCircleData Data { get; set; } = new();
}
