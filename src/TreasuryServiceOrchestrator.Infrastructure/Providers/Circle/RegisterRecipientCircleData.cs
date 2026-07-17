using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

// docs/features/10-outbound-transfers-and-recipients.md §6.1: the live create-response example
// body is `id, address, chain, currency, description` — no `status` field. Status is nullable
// here and defaults to empty string when absent; RecipientStatusMapper safely falls back to
// PendingApproval for an unrecognized/empty literal, so this never needs to guess a value Circle
// did not send.
public sealed class RegisterRecipientCircleData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
