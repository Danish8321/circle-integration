using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);

    // Tenant-scoped at the data-access layer per CLAUDE.md invariant 7: both the sub-account
    // and the caller's ClientCompanyId are required, so cross-tenant reads are structurally
    // impossible rather than filtered after the fact.
    Task<IReadOnlyList<Transaction>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, CancellationToken cancellationToken = default);

    Task<Transaction?> GetByIdAsync(
        Guid transactionId, string clientCompanyId, CancellationToken cancellationToken = default);
}
