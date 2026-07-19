using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Dtos;

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

    [JsonPropertyName("Timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("Signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("SignatureVersion")]
    public string SignatureVersion { get; set; } = string.Empty;

    [JsonPropertyName("SigningCertURL")]
    public string SigningCertURL { get; set; } = string.Empty;

    [JsonPropertyName("Subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("SubscribeURL")]
    public string? SubscribeURL { get; set; }

    [JsonPropertyName("Token")]
    public string? Token { get; set; }
}
