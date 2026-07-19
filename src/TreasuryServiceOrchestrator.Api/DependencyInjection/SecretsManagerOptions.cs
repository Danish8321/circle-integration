namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

// (assumed) Secret is stored as a flat JSON object whose keys are dotted IConfiguration paths
// (e.g. {"Circle:ApiKey": "...", "Circle:BaseUrl": "..."}) — not pinned down by ADR 0009, tighten
// once a real Production secret is provisioned.
public sealed class SecretsManagerOptions
{
    public const string SectionName = "SecretsManager";

    // Explicit opt-in, not environment alone — matches MockProviderOptions:Enabled. Keeps build
    // tooling (OpenAPI doc generation runs Program with ASPNETCORE_ENVIRONMENT unset, which
    // defaults to Production) and tests from ever attempting a real AWS call.
    public bool Enabled { get; set; }

    public string SecretId { get; set; } = string.Empty;
    public string? Region { get; set; }
}
