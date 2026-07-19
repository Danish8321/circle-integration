using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class CreateTransferCircleEnvelope
{
    [JsonPropertyName("data")]
    public CreateTransferCircleData Data { get; set; } = new();
}
