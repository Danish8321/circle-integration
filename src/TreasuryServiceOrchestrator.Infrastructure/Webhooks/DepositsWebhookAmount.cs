using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

public sealed class DepositsWebhookAmount
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = "0";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;
}
