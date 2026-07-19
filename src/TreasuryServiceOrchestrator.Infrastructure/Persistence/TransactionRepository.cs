using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class TransactionRepository(TreasuryServiceOrchestratorDbContext dbContext) : ITransactionRepository
{
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        await dbContext.Transactions.AddAsync(transaction, cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Transactions
            .Where(x => x.SubAccountId == subAccountId && x.ClientCompanyId == clientCompanyId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Transaction?> GetByIdAsync(
        Guid transactionId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Transactions
            .FirstOrDefaultAsync(
                x => x.Id == transactionId && x.ClientCompanyId == clientCompanyId,
                cancellationToken);
    }

    public async Task<Transaction?> GetByProviderReferenceIdAsync(
        string providerReferenceId, CancellationToken cancellationToken = default)
    {
        // System-context lookup (reconciliation): no tenant on the ambient caller yet, so bypass
        // the global tenant query filter explicitly. Discovers the owning tenant by provider id.
        return await dbContext.Transactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ProviderReferenceId == providerReferenceId, cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> ListAllAsync(
        TransactionListFilter filter, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Transactions.AsQueryable();

        if (filter.ClientCompanyId is not null)
        {
            query = query.Where(x => x.ClientCompanyId == filter.ClientCompanyId);
        }

        if (filter.Type is not null)
        {
            query = query.Where(x => x.Type == filter.Type);
        }

        if (filter.Status is not null)
        {
            query = query.Where(x => x.Status == filter.Status);
        }

        if (filter.FromUtc is not null)
        {
            query = query.Where(x => x.CreatedAtUtc >= filter.FromUtc);
        }

        if (filter.ToUtc is not null)
        {
            query = query.Where(x => x.CreatedAtUtc <= filter.ToUtc);
        }

        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;

        return await query
            .OrderBy(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
}
