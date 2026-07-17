using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

// Circle's real SNS-unwrapped "deposits" envelope shape (docs/features/09-deposits-and-funding.md
// §3.5): { clientId, notificationType, version, deposit: { id, walletId, amount: { amount, currency } } }.
public sealed class DepositsWebhookEnvelope
{
    [JsonPropertyName("deposit")]
    public DepositsWebhookDeposit? Deposit { get; set; }
}
