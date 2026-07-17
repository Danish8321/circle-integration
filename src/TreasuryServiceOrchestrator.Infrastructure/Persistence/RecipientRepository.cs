using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class RecipientRepository(TreasuryServiceOrchestratorDbContext dbContext) : IRecipientRepository
{
    public async Task AddAsync(Recipient recipient, CancellationToken cancellationToken = default)
    {
        await dbContext.Recipients.AddAsync(recipient, cancellationToken);
    }

    public async Task<Recipient?> FindByIdAsync(
        Guid recipientId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Recipients
            .FirstOrDefaultAsync(
                x => x.Id == recipientId && x.ClientCompanyId == clientCompanyId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Recipient>> ListForSubAccountAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Recipients
            .Where(x => x.SubAccountId == subAccountId && x.ClientCompanyId == clientCompanyId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Recipient?> FindByCircleRecipientIdAsync(
        string circleRecipientId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Recipients
            .FirstOrDefaultAsync(x => x.CircleRecipientId == circleRecipientId, cancellationToken);
    }
}
