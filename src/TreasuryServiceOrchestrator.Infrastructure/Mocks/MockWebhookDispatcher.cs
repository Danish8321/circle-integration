using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// Pulls due scheduled webhooks off <see cref="MockWebhookChannel"/> and feeds each one through
/// the real inbound webhook pipeline (<see cref="WebhookProcessor.HandleAsync"/>) exactly as if
/// it had arrived over the wire from Circle — the same entry point real SNS deliveries use after
/// inbox unwrap. There is no separate mock-only processing/dedup/persistence path.
/// </summary>
public sealed class MockWebhookDispatcher(
    MockWebhookChannel channel,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
{
    /// <summary>Dispatches every webhook currently due, each through its own DI scope.</summary>
    public async Task DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        var due = channel.DequeueDue(timeProvider.GetUtcNow().UtcDateTime);

        foreach (var webhook in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<WebhookProcessor>();
            var incoming = new IncomingWebhookEvent(webhook.Topic, $"mock-{Guid.NewGuid()}", webhook.PayloadJson);

            await processor.HandleAsync(incoming, cancellationToken);
        }
    }
}
