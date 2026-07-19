using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Compliance;

// Circle's inner notification envelope (post SNS-unwrap), per ADR 0007 / docs/features/03 §2.5:
// {clientId, notificationType, version, customAttributes, externalEntity: {...}}.
public sealed class ExternalEntityWebhookEnvelope
{
    [JsonPropertyName("notificationType")]
    public string NotificationType { get; set; } = string.Empty;

    [JsonPropertyName("externalEntity")]
    public ExternalEntityCircleData ExternalEntity { get; set; } = new();
}
