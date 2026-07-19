using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Validators;

public sealed class GenerateDepositAddressCommandValidator : AbstractValidator<GenerateDepositAddressCommand>
{
    public GenerateDepositAddressCommandValidator(SupportedChainsOptions supportedChains)
    {
        RuleFor(x => x.SubAccountId).NotEmpty();
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(16);
        RuleFor(x => x.Chain)
            .NotEmpty()
            .MaximumLength(16)
            .Must(supportedChains.IsSupported)
            .WithMessage(x => $"Chain '{x.Chain}' is not supported.");
    }
}
