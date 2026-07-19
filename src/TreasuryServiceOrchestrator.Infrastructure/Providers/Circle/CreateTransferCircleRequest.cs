using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

// docs/features/10-outbound-transfers-and-recipients.md §4.2: no Travel Rule originator
// name/address fields exist on this endpoint (CLAUDE.md invariant 12) — do not add any.
public sealed class CreateTransferCircleRequest
{
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonPropertyName("destination")]
    public CreateTransferCircleDestination Destination { get; set; } = new();

    [JsonPropertyName("amount")]
    public CreateTransferCircleAmount Amount { get; set; } = new();
}
