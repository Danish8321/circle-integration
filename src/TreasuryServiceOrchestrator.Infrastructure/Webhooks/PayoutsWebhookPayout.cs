using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

public sealed class PayoutsWebhookPayout
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("fees")]
    public string? Fees { get; set; }

    // Intentionally excluded from the processor's required-field check — absent while the
    // payout is still pending, present only once the net settlement amount is known.
    [JsonPropertyName("toAmount")]
    public string? ToAmount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}
