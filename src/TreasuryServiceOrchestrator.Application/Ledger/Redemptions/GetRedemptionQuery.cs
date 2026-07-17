namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

/// <remarks>
/// ClientCompanyId/tenant scope is not a query field — it comes from ICallerContext
/// (CLAUDE.md invariant 7).
/// </remarks>
public sealed record GetRedemptionQuery(Guid RedeemRequestId);
