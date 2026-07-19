using Microsoft.Extensions.Logging;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Services;

public sealed partial class WebhookProcessor(
    IWebhookInboxRepository inbox,
    IEnumerable<IWebhookTopicProcessor> topicProcessors,
    TimeProvider timeProvider,
    ILogger<WebhookProcessor> logger)
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Webhook dispatch failed for inbox entry {EntryId} (topic {Topic})")]
    private partial void LogDispatchFailed(Exception ex, Guid entryId, string topic);


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

        return await DispatchAsync(entry.Id, entry.Topic, entry.PayloadJson, cancellationToken);
    }

    /// <summary>
    /// Re-runs an existing dead-lettered inbox entry through the same topic-processor dispatch
    /// used by <see cref="HandleAsync"/> (Application/Admin/ReplayWebhookInboxEntryHandler) —
    /// there is no separate polling dispatcher for inbound webhooks to re-enqueue into, so
    /// replay re-invokes this pipeline directly rather than adding a bespoke second delivery
    /// path.
    /// </summary>
    public async Task<WebhookProcessingStatus> ReplayAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        var entry = await inbox.GetByIdAsync(entryId, cancellationToken)
            ?? throw new NotFoundException($"No webhook inbox entry '{entryId}'.");

        await inbox.ResetForReplayAsync(entryId, cancellationToken);

        return await DispatchAsync(entry.Id, entry.Topic, entry.PayloadJson, cancellationToken);
    }

    private async Task<WebhookProcessingStatus> DispatchAsync(
        Guid entryId, string topic, string payloadJson, CancellationToken cancellationToken)
    {
        var topicProcessor = topicProcessors.FirstOrDefault(p =>
            string.Equals(p.Topic, topic, StringComparison.Ordinal));

        if (topicProcessor is null)
        {
            // Unhandled topic: stored + acknowledged per PRD §10 item 1, never dead-lettered —
            // there is no processor to retry against, so this must not map to a retryable status.
            await inbox.MarkFailedAsync(
                entryId, $"No processor registered for topic '{topic}'.", cancellationToken);
            return WebhookProcessingStatus.Unhandled;
        }

        try
        {
            await topicProcessor.ProcessAsync(payloadJson, cancellationToken);
            await inbox.MarkProcessedAsync(entryId, cancellationToken);
            return WebhookProcessingStatus.Processed;
        }
        catch (Exception ex)
        {
            LogDispatchFailed(ex, entryId, topic);
            await inbox.MarkFailedAsync(entryId, ex.Message, cancellationToken);
            return WebhookProcessingStatus.Failed;
        }
    }
}
