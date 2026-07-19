using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Validators;

public sealed class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(x => x.RecipientId).NotEmpty();
        RuleFor(x => x.Amount).NotNull();
        RuleFor(x => x.Amount.Amount).GreaterThan(0m).When(x => x.Amount is not null);
        RuleFor(x => x.Amount.CurrencyCode).NotEmpty().MaximumLength(16).When(x => x.Amount is not null);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(128);
    }
}
