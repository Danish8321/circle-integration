namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record SetSubAccountDisabledResponse(
    Guid SubAccountId,
    string ClientCompanyId,
    bool IsDisabled);
