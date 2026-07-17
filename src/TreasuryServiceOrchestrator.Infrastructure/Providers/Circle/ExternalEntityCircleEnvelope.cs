using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class ExternalEntityCircleEnvelope
{
    [JsonPropertyName("data")]
    public ExternalEntityCircleData Data { get; set; } = new();
}
