namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ReplayWebhookInboxEntryCommand(Guid WebhookInboxEntryId, string CorrelationId);
