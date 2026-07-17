using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

public sealed class CreateRedemptionCommandValidator : AbstractValidator<CreateRedemptionCommand>
{
    public CreateRedemptionCommandValidator()
    {
        RuleFor(x => x.LinkedBankAccountId).NotEmpty();
        RuleFor(x => x.GrossAmount).NotNull();
        RuleFor(x => x.GrossAmount.Amount).GreaterThan(0m).When(x => x.GrossAmount is not null);
        RuleFor(x => x.GrossAmount.CurrencyCode).NotEmpty().MaximumLength(16).When(x => x.GrossAmount is not null);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(128);
    }
}
