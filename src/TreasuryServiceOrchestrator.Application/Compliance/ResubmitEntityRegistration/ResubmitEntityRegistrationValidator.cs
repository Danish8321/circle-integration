using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Compliance.ResubmitEntityRegistration;

public sealed class ResubmitEntityRegistrationValidator : AbstractValidator<ResubmitEntityRegistrationCommand>
{
    public ResubmitEntityRegistrationValidator()
    {
        RuleFor(x => x.ClientCompanyId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BusinessUniqueIdentifier).NotEmpty().MaximumLength(64);
        RuleFor(x => x.IdentifierIssuingCountryCode).NotEmpty().Length(2);
        RuleFor(x => x.Country).NotEmpty().Length(2);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Postcode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.StreetName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BuildingNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(128);
    }
}
