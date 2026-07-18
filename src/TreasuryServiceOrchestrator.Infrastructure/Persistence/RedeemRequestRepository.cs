using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class RedeemRequestRepository(TreasuryServiceOrchestratorDbContext dbContext)
    : IRedeemRequestRepository
{
    public async Task AddAsync(RedeemRequest redeemRequest, CancellationToken cancellationToken = default)
    {
        await dbContext.RedeemRequests.AddAsync(redeemRequest, cancellationToken);
    }

    public async Task<RedeemRequest?> GetByIdAsync(
        Guid redeemRequestId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.RedeemRequests
            .FirstOrDefaultAsync(
                x => x.Id == redeemRequestId && x.ClientCompanyId == clientCompanyId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<RedeemRequest>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.RedeemRequests
            .Where(x => x.SubAccountId == subAccountId && x.ClientCompanyId == clientCompanyId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<RedeemRequest?> FindByCircleRedeemIdAsync(
        string circleRedeemId, CancellationToken cancellationToken = default)
    {
        return await dbContext.RedeemRequests
            .FirstOrDefaultAsync(x => x.CircleRedeemId == circleRedeemId, cancellationToken);
    }
}
