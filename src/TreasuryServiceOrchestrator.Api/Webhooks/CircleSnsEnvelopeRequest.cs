using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Webhooks;

public sealed class CircleSnsEnvelopeRequest
{
    [JsonPropertyName("Type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("MessageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("TopicArn")]
    public string TopicArn { get; set; } = string.Empty;

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("Signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("SigningCertURL")]
    public string SigningCertURL { get; set; } = string.Empty;
}
