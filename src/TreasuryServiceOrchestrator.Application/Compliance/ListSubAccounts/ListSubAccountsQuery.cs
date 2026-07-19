using TreasuryServiceOrchestrator.Application.Shared;

namespace TreasuryServiceOrchestrator.Application.Compliance.ListSubAccounts;

// The only query allowed to carry a TenantScope: single-tenant handlers take a plain
// resolved ClientCompanyId string; the list handler matches on scope itself.
public sealed record ListSubAccountsQuery(
    TenantScope Scope,
    SubAccountLifecycleState? LifecycleState,
    string CorrelationId);
