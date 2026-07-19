using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class RegisterRecipientCircleEnvelope
{
    [JsonPropertyName("data")]
    public RegisterRecipientCircleData Data { get; set; } = new();
}
