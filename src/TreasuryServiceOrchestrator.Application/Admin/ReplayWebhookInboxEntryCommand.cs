namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed record ReplayWebhookInboxEntryCommand(Guid WebhookInboxEntryId, string CorrelationId);
