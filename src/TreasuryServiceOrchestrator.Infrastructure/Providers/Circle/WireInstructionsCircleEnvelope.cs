using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class WireInstructionsCircleEnvelope
{
    [JsonPropertyName("data")]
    public WireInstructionsCircleData Data { get; set; } = new();
}
