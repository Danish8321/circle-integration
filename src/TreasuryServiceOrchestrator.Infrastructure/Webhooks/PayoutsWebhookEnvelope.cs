using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

// Circle's real SNS-unwrapped "payouts" envelope shape
// (docs/features/11-redemption-and-payouts.md §7): { payout: { id, status, amount, fees,
// toAmount?, currency } }. `toAmount` is intentionally optional — absent while the payout is
// still pending, present once the actual net settlement amount is known (correction #3).
public sealed class PayoutsWebhookEnvelope
{
    [JsonPropertyName("payout")]
    public PayoutsWebhookPayout? Payout { get; set; }
}
