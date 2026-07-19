namespace TreasuryServiceOrchestrator.Infrastructure.Shared;

/// <summary>
/// Resilience configuration for the named "Circle" <see cref="System.Net.Http.HttpClient"/>.
/// Matches PRD §11.3 / docs/features/05-reliability-and-error-handling.md §4 exactly.
/// </summary>
public sealed class CircleClientOptions
{
    public const string SectionName = "CircleClient";

    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
    public int RetryCount { get; set; } = 3;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakerDurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);
}
