namespace TreasuryServiceOrchestrator.Application.Ports;

// Widened US wire-creation body shape (docs/features/08-banking-and-wire-instructions.md
// §3.2/§7, ticket 11 resolution (a)) — mirrors the widened LinkedBankAccount fields so the
// gateway can build a well-formed Circle US wire-creation body without a static
// Distributor-level config fallback. IdempotencyKey is the caller's reserved key, forwarded
// verbatim, never gateway-generated (CLAUDE.md invariant 11).
public sealed record CreateLinkedBankAccountGatewayRequest(
    string IdempotencyKey,
    string BeneficiaryName,
    string AccountNumber,
    string RoutingNumber,
    string BankName,
    string BillingName,
    string BillingCity,
    string BillingCountry,
    string BillingLine1,
    string BillingPostalCode,
    string? BillingLine2,
    string? BillingDistrict,
    string BankAddressCountry,
    string? BankAddressBankName);
