using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

/// <summary>
/// Webhook-driven: <c>Status</c> is the raw provider literal (<c>pending | running | complete |
/// failed</c>), mapped through <see cref="Transfers.TransferStatusMapper"/> (shared vocabulary,
/// <see cref="RedeemRequest"/> reuses <see cref="TransferStatus"/>). Circle's payouts webhook
/// carries an optional <c>toAmount</c> (correction #3) — that presence/absence branch is resolved
/// at the webhook mapping edge (the 07.5 webhook processor), not here: this command always carries
/// the already-resolved <c>NetAmount</c> (<c>toAmount</c> when present, else <c>amount - fees</c>)
/// and <c>Fees</c> as direct fields, keeping this handler a simple settle-or-fail state
/// transition. Both are required only on a <c>complete</c> status; ignored otherwise.
/// </summary>
public sealed record ProcessPayoutStatusCommand(
    string CircleRedeemId,
    string Status,
    Money? Fees,
    Money? NetAmount);
