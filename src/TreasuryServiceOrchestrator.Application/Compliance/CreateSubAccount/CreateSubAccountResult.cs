using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;

public sealed record CreateSubAccountResult(
    Guid SubAccountId,
    string ClientCompanyId,
    string CircleWalletId,
    SubAccountLifecycleState LifecycleState);
