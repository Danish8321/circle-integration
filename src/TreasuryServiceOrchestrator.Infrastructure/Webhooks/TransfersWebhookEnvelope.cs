using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

// Circle's real SNS-unwrapped "transfers" envelope shape
// (docs/features/10-outbound-transfers-and-recipients.md §3.5): the `transfers` topic is
// **shared** between outgoing transfers this service initiated and incoming on-chain deposits
// (doc-grilling correction #5) — { clientId, notificationType, version, transfer: { id, status,
// source: { type, id }, destination: { type, id, addressId }, amount: { amount, currency } } }.
public sealed class TransfersWebhookEnvelope
{
    [JsonPropertyName("transfer")]
    public TransfersWebhookTransfer? Transfer { get; set; }
}
