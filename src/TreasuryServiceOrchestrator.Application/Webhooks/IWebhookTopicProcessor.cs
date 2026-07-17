namespace TreasuryServiceOrchestrator.Application.Webhooks;

public interface IWebhookTopicProcessor
{
    string Topic { get; }
    Task ProcessAsync(string payloadJson, CancellationToken cancellationToken);
}
