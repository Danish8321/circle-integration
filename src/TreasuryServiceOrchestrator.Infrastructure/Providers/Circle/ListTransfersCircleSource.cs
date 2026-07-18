using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class ListTransfersCircleSource
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("chain")]
    public string? Chain { get; set; }
}
