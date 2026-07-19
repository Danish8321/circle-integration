using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record SubAccountDetailsResult(
    Guid SubAccountId,
    string ClientCompanyId,
    string LifecycleState,
    bool IsDisabled,
    string? CircleWalletId,
    string? LatestRegistrationStatus,
    string? RegistrationRejectionReason,
    Money? CurrentBalance = null);
