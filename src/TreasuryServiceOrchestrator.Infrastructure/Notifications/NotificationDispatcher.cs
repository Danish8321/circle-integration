using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Notifications;

/// <summary>
/// Polls the notification outbox for due entries and attempts delivery, applying bounded
/// exponential backoff on failure (docs/features/13-internal-notifications-outbox.md §4.3).
/// Registered singleton (driven by <see cref="NotificationDispatchBackgroundService"/>); resolves
/// its scoped dependencies through a fresh <see cref="IServiceScopeFactory"/> scope per call.
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory, IOptions<NotificationDispatcherOptions> options, TimeProvider timeProvider)
{
    public async Task<int> DispatchDueBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var settings = options.Value;

        var due = await outbox.GetDueBatchAsync(
            settings.MaxBatchSize, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        foreach (var entry in due)
        {
            var delivered = await sender.SendAsync(entry, cancellationToken);
            if (delivered)
            {
                entry.Status = NotificationDeliveryStatus.Delivered;
                entry.DeliveredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            }
            else
            {
                entry.AttemptCount++;
                var backoffMilliseconds = Math.Min(
                    settings.BaseBackoffMilliseconds * (1 << Math.Min(entry.AttemptCount, 10)),
                    settings.MaxBackoffMilliseconds);
                entry.NextAttemptAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(backoffMilliseconds);
            }
        }

        if (due.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }
}
