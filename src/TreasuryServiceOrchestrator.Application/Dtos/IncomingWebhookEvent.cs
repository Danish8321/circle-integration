namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record IncomingWebhookEvent(string Topic, string ProviderEventId, string PayloadJson);
