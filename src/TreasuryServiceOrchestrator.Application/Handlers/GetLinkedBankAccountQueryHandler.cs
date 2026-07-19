using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class GetLinkedBankAccountQueryHandler(
    ILinkedBankAccountRepository linkedBankAccounts,
    ICallerContext callerContext)
{
    public async Task<LinkedBankAccountResult> HandleAsync(
        GetLinkedBankAccountQuery query, CancellationToken cancellationToken = default)
    {
        // Tenant identity always comes from ICallerContext, never a route/body parameter
        // (invariant 7); an unidentified caller cannot look up any linked bank account.
        if (string.IsNullOrEmpty(callerContext.CallerId))
        {
            throw new TenantForbiddenException();
        }

        var linkedBankAccount = await linkedBankAccounts.GetByIdAsync(
            query.LinkedBankAccountId, callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No linked bank account '{query.LinkedBankAccountId}'.");

        return Map(linkedBankAccount);
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
