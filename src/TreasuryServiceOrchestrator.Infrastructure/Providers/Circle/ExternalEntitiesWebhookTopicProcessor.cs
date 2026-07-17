using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.ProcessExternalEntityDecision;
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

public sealed class ExternalEntitiesWebhookTopicProcessor(ProcessExternalEntityDecisionHandler handler)
    : IWebhookTopicProcessor
{
    public string Topic => "externalEntities";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<ExternalEntityWebhookEnvelope>(payloadJson)
            ?? throw new InvalidOperationException("Empty externalEntities webhook payload.");

        var command = new ProcessExternalEntityDecisionCommand(
            CircleWalletId: envelope.ExternalEntity.WalletId,
            ComplianceState: envelope.ExternalEntity.ComplianceState,
            CorrelationId: Guid.NewGuid().ToString());

        await handler.HandleAsync(command, cancellationToken);
    }
}
