using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

// docs/features/11-redemption-and-payouts.md §4 — source is always explicit, never omitted
// (CLAUDE.md invariant 12 hazard family: an omitted source silently debits the Distributor's
// Master Account wallet instead of the sub-account's).
public sealed record RedeemGatewayRequest(
    string IdempotencyKey,
    string SourceWalletId,
    string DestinationBankAccountId,
    Money GrossAmount);
