using System.Collections.Concurrent;


namespace TreasuryServiceOrchestrator.TestUtilities;

/// <summary>
/// Captures every <see cref="Schedule"/> call instead of dispatching a real mock webhook, so
/// tests can assert what a mock gateway scheduled without a real dispatcher running.
/// </summary>
public sealed class CapturingScheduler : IMockWebhookScheduler
{
    private readonly ConcurrentQueue<ScheduledCall> _scheduled = new();

    public IReadOnlyList<ScheduledCall> Scheduled => [.. _scheduled];

    public void Schedule(string topic, string payloadJson, TimeSpan delay) =>
        _scheduled.Enqueue(new ScheduledCall(topic, payloadJson, delay));

    public readonly record struct ScheduledCall(string Topic, string PayloadJson, TimeSpan Delay);
}
