using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class ListDepositsCircleDestination
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}
