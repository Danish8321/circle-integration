using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class ListDepositsCircleDeposit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("destination")]
    public ListDepositsCircleDestination Destination { get; set; } = new();

    [JsonPropertyName("amount")]
    public ListDepositsCircleAmount Amount { get; set; } = new();

    [JsonPropertyName("createDate")]
    public DateTime CreateDate { get; set; }
}
