namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>
/// In-memory queue backing <see cref="IMockWebhookScheduler"/>. Singleton, shared by mock
/// gateways (producer side, via <see cref="Schedule"/>) and <see cref="MockWebhookDispatcher"/>
/// (consumer side, via <see cref="DequeueDue"/>). A thread-safe list keyed by
/// <see cref="ScheduledMockWebhook.DeliverAtUtc"/> rather than a strict-FIFO
/// <c>System.Threading.Channels.Channel</c>, because the dispatcher must be able to skip
/// not-yet-due items without disturbing ones that are due.
/// </summary>
public sealed class MockWebhookChannel(TimeProvider timeProvider) : IMockWebhookScheduler
{
    private readonly List<ScheduledMockWebhook> pending = [];
    private readonly Lock gate = new();

    public void Schedule(string topic, string payloadJson, TimeSpan delay)
    {
        var deliverAtUtc = timeProvider.GetUtcNow().UtcDateTime + delay;
        var webhook = new ScheduledMockWebhook(topic, payloadJson, deliverAtUtc);

        lock (gate)
        {
            pending.Add(webhook);
        }
    }

    /// <summary>Removes and returns every scheduled webhook due at or before <paramref name="nowUtc"/>.</summary>
    public IReadOnlyList<ScheduledMockWebhook> DequeueDue(DateTime nowUtc)
    {
        lock (gate)
        {
            if (pending.Count == 0)
            {
                return [];
            }

            var due = pending.Where(w => w.DeliverAtUtc <= nowUtc).ToList();
            foreach (var webhook in due)
            {
                pending.Remove(webhook);
            }

            return due;
        }
    }
}
