using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Compliance;

public sealed class CreateExternalEntityCircleRequest
{
    [JsonPropertyName("businessName")]
    public string BusinessName { get; set; } = string.Empty;

    [JsonPropertyName("businessUniqueIdentifier")]
    public string BusinessUniqueIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("identifierIssuingCountryCode")]
    public string IdentifierIssuingCountryCode { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public CircleExternalEntityAddress Address { get; set; } = new();
}
