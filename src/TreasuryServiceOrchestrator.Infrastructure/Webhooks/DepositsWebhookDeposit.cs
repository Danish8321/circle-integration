using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

public sealed class DepositsWebhookDeposit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("walletId")]
    public string WalletId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public DepositsWebhookAmount Amount { get; set; } = new();
}
