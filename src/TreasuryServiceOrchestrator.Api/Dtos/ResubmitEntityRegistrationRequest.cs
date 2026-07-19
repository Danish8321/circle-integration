namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record ResubmitEntityRegistrationRequest(
    string BusinessName,
    string BusinessUniqueIdentifier,
    string IdentifierIssuingCountryCode,
    string Country,
    string State,
    string City,
    string Postcode,
    string StreetName,
    string BuildingNumber);
