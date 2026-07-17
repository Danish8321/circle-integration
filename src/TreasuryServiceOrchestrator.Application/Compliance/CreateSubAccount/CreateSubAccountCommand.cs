namespace TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;

/// <param name="ClientCompanyId">
/// The explicit target tenant the sub-account is created for (resolved via
/// <c>TenantScopeResolver</c> in the Api tier), not the caller's identity.
/// </param>
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
