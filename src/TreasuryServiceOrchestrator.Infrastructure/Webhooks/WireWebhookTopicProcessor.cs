using System.Text.Json;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>
/// Ticket 07.5: processes the real Circle "wire" SNS topic
/// (docs/features/08-banking-and-wire-instructions.md §8) — async bank-account verification. The
/// raw status literal (<c>pending | complete | failed</c>) is forwarded as-is;
/// <see cref="LinkedBankAccountStatusMapper"/> (inside the handler) owns mapping and throws on an
/// unrecognized literal — a closed vocabulary, unlike Transfers/AddressBookRecipients.
/// </summary>
public sealed class WireWebhookTopicProcessor(
    ICommandHandler<ProcessLinkedBankAccountStatusCommand, ProcessLinkedBankAccountStatusResult> processLinkedBankAccountStatusHandler)
    : IWebhookTopicProcessor
{
    public string Topic => "wire";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<WireWebhookEnvelope>(payloadJson)
            ?? throw new InvalidOperationException("Empty wire webhook payload.");

        var wire = envelope.Wire
            ?? throw new InvalidOperationException("wire webhook payload missing 'wire'.");

        if (string.IsNullOrWhiteSpace(wire.Id) || string.IsNullOrWhiteSpace(wire.Status))
        {
            throw new InvalidOperationException("wire webhook payload missing 'id' or 'status'.");
        }

        var command = new ProcessLinkedBankAccountStatusCommand(wire.Id, wire.Status);

        await processLinkedBankAccountStatusHandler.HandleAsync(command, cancellationToken);
    }
}
