using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class TransferRepository(TreasuryServiceOrchestratorDbContext dbContext) : ITransferRepository
{
    public async Task AddAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        await dbContext.Transfers.AddAsync(transfer, cancellationToken);
    }

    public async Task<Transfer?> GetByIdAsync(
        Guid transferId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Transfers
            .FirstOrDefaultAsync(
                x => x.Id == transferId && x.ClientCompanyId == clientCompanyId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Transfer>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, PageRequest pageRequest, CancellationToken cancellationToken = default)
    {
        var page = pageRequest.Page <= 0 ? 1 : pageRequest.Page;
        var pageSize = pageRequest.PageSize <= 0 ? 20 : pageRequest.PageSize;

        return await dbContext.Transfers
            .Where(x => x.SubAccountId == subAccountId && x.ClientCompanyId == clientCompanyId)
            .OrderBy(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<Transfer?> FindByCircleTransferIdAsync(
        string circleTransferId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Transfers
            .FirstOrDefaultAsync(x => x.CircleTransferId == circleTransferId, cancellationToken);
    }
}
