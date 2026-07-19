namespace TreasuryServiceOrchestrator.Application.Dtos;

public abstract record TenantScope
{
    // "SingleTenant" rather than "Single": CA1720 forbids identifiers matching
    // primitive type names (System.Single).
    public sealed record SingleTenant(string ClientCompanyId) : TenantScope;

    public sealed record AllTenants : TenantScope; // Admin list only
}
