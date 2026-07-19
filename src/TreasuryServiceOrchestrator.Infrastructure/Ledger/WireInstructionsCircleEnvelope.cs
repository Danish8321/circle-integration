using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class WireInstructionsCircleEnvelope
{
    [JsonPropertyName("data")]
    public WireInstructionsCircleData Data { get; set; } = new();
}
