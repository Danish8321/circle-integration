using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.Ports;

public interface ISubAccountRepository
{
    Task AddAsync(SubAccount subAccount, CancellationToken cancellationToken = default);
    Task<SubAccount?> GetByClientCompanyIdAsync(string clientCompanyId, CancellationToken cancellationToken = default);
}
