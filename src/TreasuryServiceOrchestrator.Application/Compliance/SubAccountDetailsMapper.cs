using TreasuryServiceOrchestrator.Application.Compliance.GetSubAccount;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance;

public static class SubAccountDetailsMapper
{
    public static SubAccountDetailsResult Map(SubAccount subAccount, EntityRegistration? registration) =>
        new(
            subAccount.Id,
            subAccount.ClientCompanyId,
            subAccount.LifecycleState.ToString(),
            subAccount.IsDisabled,
            subAccount.CircleWalletId,
            registration?.Status.ToString(),
            registration?.RejectionReason);
}
