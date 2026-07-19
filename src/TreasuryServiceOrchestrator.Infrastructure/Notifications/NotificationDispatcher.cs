using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
public sealed partial class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDispatcherOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatcher> logger)
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Notification dispatch failed for outbox entry {EntryId} (event {EventType}, correlation {CorrelationId})")]
    private partial void LogDispatchFailed(Exception ex, Guid entryId, string eventType, string? correlationId);

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
            bool delivered;
            try
            {
                delivered = await sender.SendAsync(entry, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One entry's failure must not abort the rest of the batch.
                LogDispatchFailed(ex, entry.Id, entry.EventType, entry.CorrelationId);
                delivered = false;
            }

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
