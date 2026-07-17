namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed record IncomingWebhookEvent(string Topic, string ProviderEventId, string PayloadJson);
