using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

// Circle's real SNS-unwrapped "wire" envelope shape
// (docs/features/08-banking-and-wire-instructions.md §8): async bank-account verification —
// { wire: { id, status } }, status vocabulary `pending | complete | failed`.
public sealed class WireWebhookEnvelope
{
    [JsonPropertyName("wire")]
    public WireWebhookBankAccount? Wire { get; set; }
}
