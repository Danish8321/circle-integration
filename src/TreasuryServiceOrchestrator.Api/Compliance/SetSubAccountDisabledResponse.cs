namespace TreasuryServiceOrchestrator.Api.Compliance;

public sealed record SetSubAccountDisabledResponse(
    Guid SubAccountId,
    string ClientCompanyId,
    bool IsDisabled);
