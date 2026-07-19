namespace TreasuryServiceOrchestrator.Application.Ports;

public sealed record CreateExternalEntityGatewayRequest(
    string BusinessName,
    string BusinessUniqueIdentifier,
    string IdentifierIssuingCountryCode,
    string Country,
    string State,
    string City,
    string Postcode,
    string StreetName,
    string BuildingNumber);
