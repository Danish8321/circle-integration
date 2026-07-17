using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class ProcessDepositCommandValidator : AbstractValidator<ProcessDepositCommand>
{
    public ProcessDepositCommandValidator()
    {
        RuleFor(x => x.SubAccountId).NotEmpty();
        RuleFor(x => x.Amount).NotNull();
        RuleFor(x => x.Amount.Amount).GreaterThan(0m).When(x => x.Amount is not null);
        RuleFor(x => x.Amount.CurrencyCode).NotEmpty().MaximumLength(16).When(x => x.Amount is not null);
        RuleFor(x => x.ProviderReferenceId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DepositSourceType).IsInEnum();
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(128);
    }
}
