using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Compliance;

public sealed class CircleExternalEntityAddress
{
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("postcode")]
    public string Postcode { get; set; } = string.Empty;

    [JsonPropertyName("streetName")]
    public string StreetName { get; set; } = string.Empty;

    [JsonPropertyName("buildingNumber")]
    public string BuildingNumber { get; set; } = string.Empty;
}
