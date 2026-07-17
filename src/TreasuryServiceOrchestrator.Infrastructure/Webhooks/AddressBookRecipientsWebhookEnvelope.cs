using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

// Circle's real SNS-unwrapped "addressBookRecipients" envelope shape
// (docs/features/10-outbound-transfers-and-recipients.md §3.2): the webhook vocabulary is
// `pending | inactive | active | denied` — distinct from the REST create-response vocabulary.
public sealed class AddressBookRecipientsWebhookEnvelope
{
    [JsonPropertyName("addressBookRecipient")]
    public AddressBookRecipientsWebhookRecipient? AddressBookRecipient { get; set; }
}
