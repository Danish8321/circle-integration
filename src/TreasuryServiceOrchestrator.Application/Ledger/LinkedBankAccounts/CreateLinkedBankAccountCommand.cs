namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

/// <remarks>
/// ClientCompanyId/tenant scope is not a command field — it comes from
/// <c>ICallerContext</c> inside the handler (CLAUDE.md invariant 7). <c>IdempotencyKey</c> is
/// still generated caller-side and forwarded verbatim to Circle's wire-creation call (CLAUDE.md
/// invariant 11's forwarding half), but this creation flow does not reserve it via
/// <c>IdempotencyExecutor</c> — see <see cref="CreateLinkedBankAccountCommandHandler"/>.
/// </remarks>
public sealed record CreateLinkedBankAccountCommand(
    Guid SubAccountId,
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
    string? BankAddressBankName,
    string IdempotencyKey);
