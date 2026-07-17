using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Ledger.Recipients;

public sealed class RegisterRecipientCommandValidator : AbstractValidator<RegisterRecipientCommand>
{
    public RegisterRecipientCommandValidator()
    {
        RuleFor(x => x.SubAccountId).NotEmpty();
        RuleFor(x => x.Chain).NotEmpty().MaximumLength(16);
        RuleFor(x => x.Address).NotEmpty();
        RuleFor(x => x.Label).NotEmpty();
    }
}
