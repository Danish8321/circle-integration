namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <summary>
/// ClientCompanyId/tenant scope is not a query field — it comes from ICallerContext
/// (CLAUDE.md invariant 7).
/// </summary>
public sealed record GetBalanceHistoryQuery(Guid SubAccountId);
