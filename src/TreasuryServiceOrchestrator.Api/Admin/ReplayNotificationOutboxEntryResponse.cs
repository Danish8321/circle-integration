namespace TreasuryServiceOrchestrator.Api.Admin;

public sealed record ReplayNotificationOutboxEntryResponse(
    Guid NotificationOutboxEntryId, string EventType, string Status);
