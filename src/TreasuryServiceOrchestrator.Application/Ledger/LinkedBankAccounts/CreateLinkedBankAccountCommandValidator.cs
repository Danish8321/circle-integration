using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

public sealed class CreateLinkedBankAccountCommandValidator : AbstractValidator<CreateLinkedBankAccountCommand>
{
    public CreateLinkedBankAccountCommandValidator()
    {
        RuleFor(x => x.SubAccountId).NotEmpty();
        RuleFor(x => x.BeneficiaryName).NotEmpty();
        RuleFor(x => x.AccountNumber).NotEmpty();
        RuleFor(x => x.RoutingNumber).NotEmpty();
        RuleFor(x => x.BankName).NotEmpty();
        RuleFor(x => x.BillingName).NotEmpty();
        RuleFor(x => x.BillingCity).NotEmpty();
        RuleFor(x => x.BillingCountry).NotEmpty();
        RuleFor(x => x.BillingLine1).NotEmpty();
        RuleFor(x => x.BillingPostalCode).NotEmpty();
        RuleFor(x => x.BankAddressCountry).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}
