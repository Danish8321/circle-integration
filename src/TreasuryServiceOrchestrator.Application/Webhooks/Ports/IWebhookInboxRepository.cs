namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface IWebhookInboxRepository
{
    Task<bool> TryAddAsync(WebhookInboxEntry entry, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default);
}
