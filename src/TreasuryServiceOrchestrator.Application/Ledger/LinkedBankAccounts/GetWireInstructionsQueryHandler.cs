using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

public sealed class GetWireInstructionsQueryHandler(
    ILinkedBankAccountRepository linkedBankAccounts,
    IStablecoinGateway gateway,
    ICallerContext callerContext)
{
    public async Task<WireInstructions> HandleAsync(
        GetWireInstructionsQuery query, CancellationToken cancellationToken = default)
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

        if (string.IsNullOrEmpty(linkedBankAccount.CircleBankAccountId))
        {
            throw new ConflictException(
                $"Linked bank account '{linkedBankAccount.Id}' has no provider reference yet.");
        }

        return await gateway.GetWireInstructionsAsync(linkedBankAccount.CircleBankAccountId, cancellationToken);
    }
}
