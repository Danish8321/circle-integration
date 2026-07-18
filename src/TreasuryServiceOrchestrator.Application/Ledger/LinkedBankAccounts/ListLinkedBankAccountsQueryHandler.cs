using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

public sealed class ListLinkedBankAccountsQueryHandler(
    ILinkedBankAccountRepository linkedBankAccounts,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<LinkedBankAccountResult>> HandleAsync(
        ListLinkedBankAccountsQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot list any sub-account's linked bank
        // accounts.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var listed = await linkedBankAccounts.ListBySubAccountAsync(
            query.SubAccountId, callerContext.CallerId, query.PageRequest ?? new PageRequest(), cancellationToken);

        return listed.Select(Map).ToList();
    }

    private static LinkedBankAccountResult Map(LinkedBankAccount linkedBankAccount) => new(
        linkedBankAccount.Id,
        linkedBankAccount.SubAccountId,
        linkedBankAccount.BeneficiaryName,
        linkedBankAccount.BankName,
        linkedBankAccount.CircleBankAccountId,
        linkedBankAccount.Status,
        linkedBankAccount.CreatedAtUtc);
}
