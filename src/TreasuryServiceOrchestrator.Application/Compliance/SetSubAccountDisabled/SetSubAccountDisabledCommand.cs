namespace TreasuryServiceOrchestrator.Application.Compliance.SetSubAccountDisabled;

public sealed record SetSubAccountDisabledCommand(
    string ClientCompanyId,
    bool Disabled,
    string CorrelationId);
