using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

public sealed class CreateTransferCircleDestination
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "verified_blockchain";

    [JsonPropertyName("addressId")]
    public string AddressId { get; set; } = string.Empty;
}
