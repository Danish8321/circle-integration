namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <remarks>
/// ClientCompanyId/tenant scope is not a query field — it comes from ICallerContext
/// (CLAUDE.md invariant 7).
/// </remarks>
public sealed record GetRedemptionQuery(Guid RedeemRequestId);
