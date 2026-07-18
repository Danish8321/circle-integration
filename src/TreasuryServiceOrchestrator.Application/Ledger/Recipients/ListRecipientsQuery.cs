using TreasuryServiceOrchestrator.Application.Shared;

namespace TreasuryServiceOrchestrator.Application.Ledger.Recipients;

/// <remarks>
/// ClientCompanyId/tenant scope is not a query field — it comes from ICallerContext
/// (CLAUDE.md invariant 7).
/// </remarks>
public sealed record ListRecipientsQuery(Guid SubAccountId, PageRequest? PageRequest = null);
