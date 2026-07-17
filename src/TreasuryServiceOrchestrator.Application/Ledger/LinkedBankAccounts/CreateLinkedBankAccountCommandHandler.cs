using FluentValidation;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

/// <summary>
/// Creates a linked bank account: validate, call the provider's wire-creation endpoint, persist
/// the resulting <see cref="LinkedBankAccount"/>. Documented, ticket-scoped exception to CLAUDE.md
/// invariant 11's reserve/gateway/complete + two-<c>SaveChangesAsync</c> shape: bank-account
/// linking has no <c>ClientCompanyId</c>-keyed money-moving reservation to make (it is not itself
/// a money-moving operation), so there is nothing for <see cref="IIdempotencyService"/>/
/// <c>IdempotencyExecutor</c> to key a replay-safe reservation by here — this handler makes a
/// single <c>SaveChangesAsync</c> call. <c>IdempotencyKey</c> is still caller-supplied and
/// forwarded verbatim to Circle on the gateway call (invariant 11's forwarding half still holds).
/// </summary>
public sealed class CreateLinkedBankAccountCommandHandler(
    ILinkedBankAccountRepository linkedBankAccounts,
    IStablecoinGateway gateway,
    IUnitOfWork unitOfWork,
    IValidator<CreateLinkedBankAccountCommand> validator,
    TimeProvider timeProvider,
    ICallerContext callerContext)
    : ICommandHandler<CreateLinkedBankAccountCommand, LinkedBankAccountResult>
{
    public async Task<LinkedBankAccountResult> HandleAsync(
        CreateLinkedBankAccountCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var gatewayResult = await gateway.CreateLinkedBankAccountAsync(
            new CreateLinkedBankAccountGatewayRequest(
                command.IdempotencyKey,
                command.BeneficiaryName,
                command.AccountNumber,
                command.RoutingNumber,
                command.BankName,
                command.BillingName,
                command.BillingCity,
                command.BillingCountry,
                command.BillingLine1,
                command.BillingPostalCode,
                command.BillingLine2,
                command.BillingDistrict,
                command.BankAddressCountry,
                command.BankAddressBankName),
            cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var linkedBankAccount = LinkedBankAccount.Create(
            command.SubAccountId,
            callerContext.CallerId,
            command.BeneficiaryName,
            command.AccountNumber,
            command.RoutingNumber,
            command.BankName,
            command.BillingName,
            command.BillingCity,
            command.BillingCountry,
            command.BillingLine1,
            command.BillingPostalCode,
            command.BillingLine2,
            command.BillingDistrict,
            command.BankAddressCountry,
            command.BankAddressBankName,
            nowUtc);

        linkedBankAccount.SetProviderReference(gatewayResult.CircleBankAccountId, nowUtc);

        var status = LinkedBankAccountStatusMapper.Map(gatewayResult.Status);
        if (status != LinkedBankAccountStatus.Pending)
        {
            linkedBankAccount.UpdateStatus(status, nowUtc);
        }

        await linkedBankAccounts.AddAsync(linkedBankAccount, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

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
