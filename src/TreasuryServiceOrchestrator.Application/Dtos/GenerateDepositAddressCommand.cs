namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <remarks>
/// ClientCompanyId/tenant scope is not a command field — it comes from
/// <c>ICallerContext</c> inside the handler (CLAUDE.md invariant 7).
/// </remarks>
public sealed record GenerateDepositAddressCommand(
    Guid SubAccountId,
    string Chain,
    string Currency);
