namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record SetSubAccountDisabledCommand(
    string ClientCompanyId,
    bool Disabled,
    string CorrelationId);
