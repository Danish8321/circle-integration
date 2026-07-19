namespace TreasuryServiceOrchestrator.Infrastructure.Shared;

public sealed class CircleOptions
{
    public const string SectionName = "Circle";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
