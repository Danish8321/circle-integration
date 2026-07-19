using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ports;

public interface INotificationOutboxRepository
{
    Task AddAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationOutboxEntry>> GetDueBatchAsync(
        int batchSize, DateTime nowUtc, CancellationToken cancellationToken);

    Task<NotificationOutboxEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
