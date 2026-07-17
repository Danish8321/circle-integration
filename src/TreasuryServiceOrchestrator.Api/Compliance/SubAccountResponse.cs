namespace TreasuryServiceOrchestrator.Api.Compliance;

public sealed record SubAccountResponse(
    Guid SubAccountId,
    string ClientCompanyId,
    string LifecycleState,
    bool IsDisabled,
    string? CircleWalletId,
    string? LatestRegistrationStatus,
    string? RegistrationRejectionReason);
