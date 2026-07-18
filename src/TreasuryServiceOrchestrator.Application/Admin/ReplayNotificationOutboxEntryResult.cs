using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed record ReplayNotificationOutboxEntryResult(
    Guid NotificationOutboxEntryId, string EventType, NotificationDeliveryStatus Status);
