using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record CreateLinkedBankAccountRequest(
    [property: JsonRequired] string BeneficiaryName,
    [property: JsonRequired] string AccountNumber,
    [property: JsonRequired] string RoutingNumber,
    [property: JsonRequired] string BankName,
    [property: JsonRequired] string BillingName,
    [property: JsonRequired] string BillingCity,
    [property: JsonRequired] string BillingCountry,
    [property: JsonRequired] string BillingLine1,
    [property: JsonRequired] string BillingPostalCode,
    string? BillingLine2,
    string? BillingDistrict,
    [property: JsonRequired] string BankAddressCountry,
    string? BankAddressBankName);
