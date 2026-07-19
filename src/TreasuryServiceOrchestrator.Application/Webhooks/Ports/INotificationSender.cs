using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface INotificationSender
{
    Task<bool> SendAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken);
}
