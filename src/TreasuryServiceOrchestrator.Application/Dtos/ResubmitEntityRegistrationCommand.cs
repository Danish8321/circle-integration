namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <param name="ClientCompanyId">
/// The target tenant whose rejected registration is being resubmitted (resolved via
/// <c>TenantScopeResolver</c> in the Api tier), not necessarily the caller's identity.
/// </param>
public sealed record ResubmitEntityRegistrationCommand(
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
