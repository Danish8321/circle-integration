namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record SubAccountResponse(
    Guid SubAccountId,
    string ClientCompanyId,
    string LifecycleState,
    bool IsDisabled,
    string? CircleWalletId,
    string? LatestRegistrationStatus,
    string? RegistrationRejectionReason);
