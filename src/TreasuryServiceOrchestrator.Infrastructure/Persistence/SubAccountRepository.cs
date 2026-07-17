using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class SubAccountRepository(TreasuryServiceOrchestratorDbContext dbContext) : ISubAccountRepository
{
    public async Task AddAsync(SubAccount subAccount, CancellationToken cancellationToken = default)
    {
        await dbContext.SubAccounts.AddAsync(subAccount, cancellationToken);
    }

    public async Task<SubAccount?> GetByClientCompanyIdAsync(
        string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.SubAccounts
            .FirstOrDefaultAsync(x => x.ClientCompanyId == clientCompanyId, cancellationToken);
    }
}
