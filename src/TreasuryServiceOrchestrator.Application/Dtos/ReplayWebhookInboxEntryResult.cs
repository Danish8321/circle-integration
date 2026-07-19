
namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ReplayWebhookInboxEntryResult(
    Guid WebhookInboxEntryId, string Topic, WebhookProcessingStatus Status);
