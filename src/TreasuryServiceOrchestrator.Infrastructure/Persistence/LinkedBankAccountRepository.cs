using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class LinkedBankAccountRepository(TreasuryServiceOrchestratorDbContext dbContext)
    : ILinkedBankAccountRepository
{
    public async Task AddAsync(LinkedBankAccount linkedBankAccount, CancellationToken cancellationToken = default)
    {
        await dbContext.LinkedBankAccounts.AddAsync(linkedBankAccount, cancellationToken);
    }

    public async Task<LinkedBankAccount?> GetByIdAsync(
        Guid linkedBankAccountId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.LinkedBankAccounts
            .FirstOrDefaultAsync(
                x => x.Id == linkedBankAccountId && x.ClientCompanyId == clientCompanyId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<LinkedBankAccount>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, PageRequest pageRequest, CancellationToken cancellationToken = default)
    {
        var page = pageRequest.Page <= 0 ? 1 : pageRequest.Page;
        var pageSize = pageRequest.PageSize <= 0 ? 20 : pageRequest.PageSize;

        return await dbContext.LinkedBankAccounts
            .Where(x => x.SubAccountId == subAccountId && x.ClientCompanyId == clientCompanyId)
            .OrderBy(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<LinkedBankAccount?> FindByCircleBankAccountIdAsync(
        string circleBankAccountId, CancellationToken cancellationToken = default)
    {
        return await dbContext.LinkedBankAccounts
            .FirstOrDefaultAsync(x => x.CircleBankAccountId == circleBankAccountId, cancellationToken);
    }
}
