using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

public sealed class TransfersWebhookTransfer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public TransfersWebhookParty? Source { get; set; }

    // The incoming-deposit branch resolves the owning SubAccount from
    // `destination.id` (our wallet id) — mirrors DepositsWebhookTopicProcessor's
    // ISubAccountRepository.GetByCircleWalletIdAsync usage for the fiat-wire path.
    [JsonPropertyName("destination")]
    public TransfersWebhookParty? Destination { get; set; }

    [JsonPropertyName("amount")]
    public TransfersWebhookAmount Amount { get; set; } = new();
}
