using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record CreateSubAccountResult(
    Guid SubAccountId,
    string ClientCompanyId,
    string CircleWalletId,
    SubAccountLifecycleState LifecycleState);
