using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IFundAccountRepository
{
    Task<FundAccount?> FindByClientCompanyIdAsync(
        string clientCompanyId, CancellationToken cancellationToken = default);

    Task AddAsync(FundAccount fundAccount, CancellationToken cancellationToken = default);
}
