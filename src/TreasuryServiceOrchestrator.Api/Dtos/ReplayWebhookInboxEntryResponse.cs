namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record ReplayWebhookInboxEntryResponse(Guid WebhookInboxEntryId, string Topic, string Status);
