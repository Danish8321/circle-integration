using System.Text.Json;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>
/// Ticket 05.4: processes the real Circle "addressBookRecipients" SNS topic
/// (docs/features/10-outbound-transfers-and-recipients.md §3.2) — deserializes the real
/// envelope's <c>addressBookRecipient.{id,status}</c> shape and dispatches into
/// <see cref="ProcessRecipientDecisionHandler"/>. Placed under Infrastructure (not
/// Application/Webhooks, as the feature doc's narrative text suggests) to match existing
/// precedent: both other <see cref="IWebhookTopicProcessor"/> implementations
/// (<c>DepositsWebhookTopicProcessor</c>, <c>ExternalEntitiesWebhookTopicProcessor</c>) already
/// live in Infrastructure even though the interface itself lives in Application.
/// </summary>
public sealed class AddressBookRecipientsWebhookTopicProcessor(
    ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult> processRecipientDecisionHandler)
    : IWebhookTopicProcessor
{
    public string Topic => "addressBookRecipients";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<AddressBookRecipientsWebhookEnvelope>(payloadJson)
            ?? throw new InvalidOperationException("Empty addressBookRecipients webhook payload.");

        var recipient = envelope.AddressBookRecipient
            ?? throw new InvalidOperationException(
                "addressBookRecipients webhook payload missing 'addressBookRecipient'.");

        if (string.IsNullOrWhiteSpace(recipient.Id) || string.IsNullOrWhiteSpace(recipient.Status))
        {
            throw new InvalidOperationException(
                "addressBookRecipients webhook payload missing 'id' or 'status'.");
        }

        var command = new ProcessRecipientDecisionCommand(recipient.Id, recipient.Status);

        await processRecipientDecisionHandler.HandleAsync(command, cancellationToken);
    }
}
