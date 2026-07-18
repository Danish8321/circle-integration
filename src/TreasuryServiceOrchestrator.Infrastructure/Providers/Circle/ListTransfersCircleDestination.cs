using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class ListTransfersCircleDestination
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("chain")]
    public string? Chain { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
