using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

// This product only ever constructs a "wire" destination (fiat-wire-only, PRD §8) even though
// Circle's BusinessDestinationRequest schema also supports cubix/pix/sepa/sepa_instant.
public sealed class RedeemCircleDestination
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "wire";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}
