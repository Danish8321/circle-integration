using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Compliance.GetSubAccount;

public sealed class GetSubAccountHandler(
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations)
{
    public async Task<SubAccountDetailsResult> HandleAsync(
        GetSubAccountQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account for client company '{query.ClientCompanyId}'.");

        var registration = await entityRegistrations.GetLatestForSubAccountAsync(subAccount.Id, cancellationToken);

        return new SubAccountDetailsResult(
            subAccount.Id,
            subAccount.ClientCompanyId,
            subAccount.LifecycleState.ToString(),
            subAccount.IsDisabled,
            subAccount.CircleWalletId,
            registration?.Status.ToString(),
            registration?.RejectionReason);
    }
}
