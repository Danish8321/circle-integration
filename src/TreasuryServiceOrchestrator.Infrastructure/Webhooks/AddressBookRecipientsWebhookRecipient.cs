using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

public sealed class AddressBookRecipientsWebhookRecipient
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
