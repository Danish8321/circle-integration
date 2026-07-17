using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

/// <remarks>
/// ClientCompanyId/tenant scope is not a command field — it comes from
/// <c>ICallerContext</c> inside the handler (CLAUDE.md invariant 7). <c>SubAccountId</c> is not a
/// field either — it is read off the resolved <see cref="LinkedBankAccount"/> (mirrors
/// <c>CreateTransferCommand</c> resolving it off the recipient). <c>GrossAmount</c> is validated
/// and reserved with the provider at creation time, not debited from the ledger yet
/// (docs/features/11-redemption-and-payouts.md §4) — the debit happens later on the
/// webhook-driven Complete transition, see <see cref="ProcessPayoutStatusCommandHandler"/>.
/// </remarks>
public sealed record CreateRedemptionCommand(
    Guid LinkedBankAccountId,
    Money GrossAmount,
    string IdempotencyKey,
    string CorrelationId);
