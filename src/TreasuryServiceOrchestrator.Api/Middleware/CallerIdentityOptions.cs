namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class CallerIdentityOptions
{
    public const string SectionName = "CallerIdentity";

    public string AdminCallerId { get; set; } = string.Empty;
}
