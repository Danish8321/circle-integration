using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class WebhookProcessor(
    IWebhookInboxRepository inbox,
    IEnumerable<IWebhookTopicProcessor> topicProcessors,
    TimeProvider timeProvider)
{
    public async Task<WebhookProcessingStatus> HandleAsync(
        IncomingWebhookEvent incoming, CancellationToken cancellationToken = default)
    {
        var entry = new WebhookInboxEntry
        {
            Id = Guid.NewGuid(),
            Topic = incoming.Topic,
            CircleEventId = incoming.ProviderEventId,
            PayloadJson = incoming.PayloadJson,
            ReceivedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Processed = false,
        };

        if (!await inbox.TryAddAsync(entry, cancellationToken))
        {
            return WebhookProcessingStatus.Processed;
        }

        var topicProcessor = topicProcessors.FirstOrDefault(p =>
            string.Equals(p.Topic, incoming.Topic, StringComparison.Ordinal));

        if (topicProcessor is null)
        {
            // Unhandled topic: stored + acknowledged per PRD §10 item 1, never dead-lettered —
            // there is no processor to retry against, so this must not map to a retryable status.
            await inbox.MarkFailedAsync(
                entry.Id, $"No processor registered for topic '{incoming.Topic}'.", cancellationToken);
            return WebhookProcessingStatus.Unhandled;
        }

        try
        {
            await topicProcessor.ProcessAsync(incoming.PayloadJson, cancellationToken);
            await inbox.MarkProcessedAsync(entry.Id, cancellationToken);
            return WebhookProcessingStatus.Processed;
        }
        catch (Exception ex)
        {
            await inbox.MarkFailedAsync(entry.Id, ex.Message, cancellationToken);
            return WebhookProcessingStatus.Failed;
        }
    }
}
