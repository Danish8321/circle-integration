namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record ReplayNotificationOutboxEntryResponse(
    Guid NotificationOutboxEntryId, string EventType, string Status);
