namespace TreasuryServiceOrchestrator.Application.Compliance.SetSubAccountDisabled;

public sealed record SetSubAccountDisabledResult(
    Guid SubAccountId,
    string ClientCompanyId,
    bool IsDisabled);
