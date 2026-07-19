using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class CreateLinkedBankAccountCircleEnvelope
{
    [JsonPropertyName("data")]
    public CreateLinkedBankAccountCircleData Data { get; set; } = new();
}
