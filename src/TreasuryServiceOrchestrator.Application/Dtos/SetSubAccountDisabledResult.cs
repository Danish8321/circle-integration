namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record SetSubAccountDisabledResult(
    Guid SubAccountId,
    string ClientCompanyId,
    bool IsDisabled);
