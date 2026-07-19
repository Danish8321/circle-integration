using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ports;

public interface ILinkedBankAccountRepository
{
    Task AddAsync(LinkedBankAccount linkedBankAccount, CancellationToken cancellationToken = default);

    // Tenant-scoped at the data-access layer per CLAUDE.md invariant 7: both the account id
    // and the caller's ClientCompanyId are required, so cross-tenant reads are structurally
    // impossible rather than filtered after the fact.
    Task<LinkedBankAccount?> GetByIdAsync(
        Guid linkedBankAccountId, string clientCompanyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LinkedBankAccount>> ListBySubAccountAsync(
        Guid subAccountId, string clientCompanyId, PageRequest pageRequest, CancellationToken cancellationToken = default);

    // For webhook correlation: Circle's wire webhooks carry only the provider-side bank
    // account id, not our tenant identity, so this lookup is not tenant-scoped by parameter —
    // the caller resolves ClientCompanyId from the returned LinkedBankAccount itself.
    Task<LinkedBankAccount?> FindByCircleBankAccountIdAsync(
        string circleBankAccountId, CancellationToken cancellationToken = default);
}
