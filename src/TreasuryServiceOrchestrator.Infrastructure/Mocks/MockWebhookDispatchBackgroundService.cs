using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// Polls <see cref="MockWebhookDispatcher"/> for the life of the host, at
/// <see cref="MockProviderOptions.WebhookDelayMilliseconds"/>. Only registered as a hosted
/// service when mock mode is enabled (wiring decision belongs to <c>Program.cs</c>, out of
/// scope here) — this class does not itself check <see cref="MockProviderOptions.Enabled"/>.
/// </summary>
public sealed class MockWebhookDispatchBackgroundService(
    MockWebhookDispatcher dispatcher,
    IOptions<MockProviderOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.WebhookDelayMilliseconds));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await dispatcher.DispatchDueAsync(stoppingToken);
                await Task.Delay(pollInterval, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — swallow so the service stops cleanly.
        }
    }
}
