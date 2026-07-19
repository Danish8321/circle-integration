using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ports;

public interface INotificationSender
{
    Task<bool> SendAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken);
}
