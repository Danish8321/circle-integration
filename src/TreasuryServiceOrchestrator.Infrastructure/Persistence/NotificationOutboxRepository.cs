using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class NotificationOutboxRepository(TreasuryServiceOrchestratorDbContext dbContext)
    : INotificationOutboxRepository
{
    public async Task AddAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken) =>
        await dbContext.NotificationOutboxEntries.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<NotificationOutboxEntry>> GetDueBatchAsync(
        int batchSize, DateTime nowUtc, CancellationToken cancellationToken) =>
        // Background dispatcher: no HTTP caller, spans all tenants. Bypass the global tenant
        // query filter (INV7 backstop) — delivery is a system concern, not a tenant read.
        await dbContext.NotificationOutboxEntries
            .IgnoreQueryFilters()
            .Where(e => e.Status == NotificationDeliveryStatus.Pending
                && (e.NextAttemptAtUtc == null || e.NextAttemptAtUtc <= nowUtc))
            .OrderBy(e => e.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<NotificationOutboxEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.NotificationOutboxEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
}
