using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Dtos;

// Circle's own envelope inside the SNS Message field (docs/features/03 §2.5):
// {clientId, notificationType, version, customAttributes, <resourceKey>: {...}}.
// Only the dispatch key is read at the controller boundary; the rest is forwarded
// verbatim (as PayloadJson) to the topic-specific processor.
public sealed class CircleNotificationEnvelope
{
    [JsonPropertyName("notificationType")]
    public string NotificationType { get; set; } = string.Empty;
}
