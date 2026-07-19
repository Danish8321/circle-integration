namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public bool Enabled { get; init; }

    public string OtlpEndpoint { get; init; } = "http://localhost:4317";
}
