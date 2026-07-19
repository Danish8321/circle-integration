namespace TreasuryServiceOrchestrator.Application.Ports;

public interface IWebhookTopicProcessor
{
    string Topic { get; }
    Task ProcessAsync(string payloadJson, CancellationToken cancellationToken);
}
