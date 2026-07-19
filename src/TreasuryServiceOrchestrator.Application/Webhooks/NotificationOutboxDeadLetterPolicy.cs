
namespace TreasuryServiceOrchestrator.Application.Webhooks;

public static class NotificationOutboxDeadLetterPolicy
{
    // Mirrors WebhookDeadLetterPolicy.AttemptThreshold — decided during the 2026-07-17 grilling
    // session (see CONTEXT.md, "Dead-lettered").
    public const int AttemptThreshold = 5;

    public static bool IsDeadLettered(this NotificationOutboxEntry entry) =>
        entry.Status != NotificationDeliveryStatus.Delivered && entry.AttemptCount >= AttemptThreshold;
}
