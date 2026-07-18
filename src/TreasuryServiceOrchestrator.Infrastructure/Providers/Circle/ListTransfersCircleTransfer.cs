using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class ListTransfersCircleTransfer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public ListTransfersCircleSource Source { get; set; } = new();

    [JsonPropertyName("destination")]
    public ListTransfersCircleDestination Destination { get; set; } = new();

    [JsonPropertyName("amount")]
    public ListTransfersCircleAmount Amount { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("createDate")]
    public DateTime CreateDate { get; set; }
}
