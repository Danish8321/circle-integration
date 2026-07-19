namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record CreateSubAccountResponse(
    Guid SubAccountId,
    string ClientCompanyId,
    string CircleWalletId,
    string LifecycleState);
