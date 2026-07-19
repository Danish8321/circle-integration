using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ReplayNotificationOutboxEntryResult(
    Guid NotificationOutboxEntryId, string EventType, NotificationDeliveryStatus Status);
