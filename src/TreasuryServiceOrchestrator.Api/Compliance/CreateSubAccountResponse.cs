namespace TreasuryServiceOrchestrator.Api.Compliance;

public sealed record CreateSubAccountResponse(
    Guid SubAccountId,
    string ClientCompanyId,
    string CircleWalletId,
    string LifecycleState);
