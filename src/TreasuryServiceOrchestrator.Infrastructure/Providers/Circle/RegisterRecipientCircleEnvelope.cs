using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class RegisterRecipientCircleEnvelope
{
    [JsonPropertyName("data")]
    public RegisterRecipientCircleData Data { get; set; } = new();
}
