using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

// docs/features/11-redemption-and-payouts.md §7: `source` is always set explicitly, never
// omitted — an omitted `source` silently defaults to the Distributor's Master Account wallet
// (CLAUDE.md invariant 12 hazard family).
public sealed class RedeemCircleRequest
{
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public RedeemCircleSource Source { get; set; } = new();

    [JsonPropertyName("destination")]
    public RedeemCircleDestination Destination { get; set; } = new();
}
