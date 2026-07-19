
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

    // Cross-tenant by design: reconciliation (Ticket 15) doesn't know which tenant a provider
    // deposit belongs to until after this dedup lookup resolves it — same shape as the
    // admin-only ListAllAsync exception to CLAUDE.md invariant 7, but here the caller is
    // trusted Application-tier reconciliation code, not a route/body parameter.
    Task<Transaction?> GetByProviderReferenceIdAsync(
        string providerReferenceId, CancellationToken cancellationToken = default);

    // Deliberate exception to CLAUDE.md invariant 7 (no route/body ClientCompanyId, always
    // ICallerContext): this is an admin-only, all-tenant read with no ClientCompanyId filter
    // required. It is safe because the caller-side Admin gate is enforced structurally by
    // AdminTransactionsController (08.3) before this method is ever called — a SubAccount
    // caller never reaches this query/repository layer at all, so there is no risk of an
    // unscoped query leaking to a non-admin caller.
    Task<IReadOnlyList<Transaction>> ListAllAsync(
        TransactionListFilter filter, CancellationToken cancellationToken = default);
}
