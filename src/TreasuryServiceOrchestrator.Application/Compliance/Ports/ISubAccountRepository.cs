using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.Ports;

public interface ISubAccountRepository
{
    Task AddAsync(SubAccount subAccount, CancellationToken cancellationToken = default);
    Task<SubAccount?> GetByClientCompanyIdAsync(string clientCompanyId, CancellationToken cancellationToken = default);
    Task<SubAccount?> GetByCircleWalletIdAsync(string circleWalletId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubAccount>> ListAsync(
        SubAccountLifecycleState? lifecycleState = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubAccount>> ListActiveWithWalletAsync(CancellationToken ct = default);
}
