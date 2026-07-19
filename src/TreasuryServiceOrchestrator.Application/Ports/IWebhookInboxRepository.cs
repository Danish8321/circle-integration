namespace TreasuryServiceOrchestrator.Application.Ports;

public interface IWebhookInboxRepository
{
    Task<bool> TryAddAsync(WebhookInboxEntry entry, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default);
    Task<WebhookInboxEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears Attempts/Processed/LastError/ProcessingResult ahead of a replay
    /// (Application/Admin/ReplayWebhookInboxEntryHandler), so the entry no longer reports as
    /// dead-lettered (WebhookDeadLetterPolicy) before the existing dispatch pipeline
    /// (WebhookProcessor) re-runs it.
    /// </summary>
    Task ResetForReplayAsync(Guid id, CancellationToken cancellationToken = default);
}
