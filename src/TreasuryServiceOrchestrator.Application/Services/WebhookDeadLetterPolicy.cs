namespace TreasuryServiceOrchestrator.Application.Services;

public static class WebhookDeadLetterPolicy
{
    // No threshold was specified in source docs; decided during the 2026-07-17 grilling
    // session (see CONTEXT.md, "Dead-lettered").
    public const int AttemptThreshold = 5;

    public static bool IsDeadLettered(this WebhookInboxEntry entry) =>
        !entry.Processed && entry.Attempts >= AttemptThreshold;
}
