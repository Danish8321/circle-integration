using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Compliance;

public sealed class ExternalEntityCircleEnvelope
{
    [JsonPropertyName("data")]
    public ExternalEntityCircleData Data { get; set; } = new();
}
