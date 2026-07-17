using FluentValidation;

namespace TreasuryServiceOrchestrator.Api.Compliance;

public sealed class CreateSubAccountRequestValidator : AbstractValidator<CreateSubAccountRequest>
{
    public CreateSubAccountRequestValidator()
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
    }
}
