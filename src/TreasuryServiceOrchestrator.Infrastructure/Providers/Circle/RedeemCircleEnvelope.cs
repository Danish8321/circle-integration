using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class RedeemCircleEnvelope
{
    [JsonPropertyName("data")]
    public RedeemCircleData Data { get; set; } = new();
}
