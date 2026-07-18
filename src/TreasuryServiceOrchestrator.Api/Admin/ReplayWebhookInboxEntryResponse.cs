namespace TreasuryServiceOrchestrator.Api.Admin;

public sealed record ReplayWebhookInboxEntryResponse(Guid WebhookInboxEntryId, string Topic, string Status);
