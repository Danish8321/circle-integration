namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>Options bound from the "Notifications" config section, controlling the internal
/// notification outbox dispatcher (docs/features/13-internal-notifications-outbox.md §4.2).</summary>
public sealed class NotificationDispatcherOptions
{
    public const string SectionName = "Notifications";

    public string EndpointUrl { get; set; } = "http://localhost:5080/internal/notifications";

    public string? AuthHeaderName { get; set; }

    public string? AuthHeaderValue { get; set; }

    public int MaxBatchSize { get; set; } = 20;

    public int PollingIntervalMilliseconds { get; set; } = 500;

    public int BaseBackoffMilliseconds { get; set; } = 1000;

    public int MaxBackoffMilliseconds { get; set; } = 60000;
}
