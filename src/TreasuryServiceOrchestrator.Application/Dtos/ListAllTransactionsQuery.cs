namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <summary>
/// Admin-only, all-tenant transaction listing. ClientCompanyId tenant scoping is intentionally
/// absent from this query — the Admin gate (ICallerContext.IsAdmin) is enforced by
/// AdminTransactionsController (08.3), not at this query/repository layer, since this endpoint
/// has no per-tenant route segment for TenantScopeResolver to arbitrate against.
/// </summary>
public sealed record ListAllTransactionsQuery(TransactionListFilter Filter);
