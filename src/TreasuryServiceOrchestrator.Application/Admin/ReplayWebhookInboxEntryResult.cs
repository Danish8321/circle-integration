using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed record ReplayWebhookInboxEntryResult(
    Guid WebhookInboxEntryId, string Topic, WebhookProcessingStatus Status);
