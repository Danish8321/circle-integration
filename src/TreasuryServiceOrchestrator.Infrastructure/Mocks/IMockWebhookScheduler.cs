namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// Port used by mock provider gateways to schedule a simulated webhook for future delivery
/// through the real inbound webhook pipeline. Abstracted so tests can supply a capturing
/// implementation instead of the real in-memory channel.
/// </summary>
public interface IMockWebhookScheduler
{
    /// <summary>
    /// Schedules <paramref name="payloadJson"/> for delivery on <paramref name="topic"/> after
    /// <paramref name="delay"/>. Implementations resolve the absolute delivery time internally
    /// via an injected <see cref="TimeProvider"/> — never <c>DateTime.UtcNow</c> directly.
    /// </summary>
    void Schedule(string topic, string payloadJson, TimeSpan delay);
}
