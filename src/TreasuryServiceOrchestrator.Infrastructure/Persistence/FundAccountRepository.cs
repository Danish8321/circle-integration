using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class FundAccountRepository(TreasuryServiceOrchestratorDbContext dbContext) : IFundAccountRepository
{
    public async Task<FundAccount?> FindByClientCompanyIdAsync(
        string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.FundAccounts
            .FirstOrDefaultAsync(x => x.ClientCompanyId == clientCompanyId, cancellationToken);
    }

    public async Task AddAsync(FundAccount fundAccount, CancellationToken cancellationToken = default)
    {
        await dbContext.FundAccounts.AddAsync(fundAccount, cancellationToken);
    }

    public async Task<IReadOnlyList<FundAccount>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.FundAccounts.ToListAsync(cancellationToken);
    }
}
