namespace TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;

public sealed record CreateSubAccountCommand(
    string ClientCompanyId,
    string BusinessName,
    string BusinessUniqueIdentifier,
    string IdentifierIssuingCountryCode,
    string Country,
    string State,
    string City,
    string Postcode,
    string StreetName,
    string BuildingNumber,
    string IdempotencyKey,
    string CorrelationId);
