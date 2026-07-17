using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

/// <summary>
/// Webhook-driven: settles or fails a <see cref="RedeemRequest"/> from a raw provider status
/// literal. The gross amount was already validated and reserved with the provider at
/// redemption-creation time (<see cref="CreateRedemptionCommandHandler"/>), not debited from the
/// ledger — the debit happens here, on the Complete transition, using
/// <see cref="RedeemRequest.GrossAmount"/> (the reserved/validated amount), not the webhook's
/// resolved net amount (docs/features/11-redemption-and-payouts.md §4). Mirrors
/// <see cref="ProcessTransferStatusCommandHandler"/>'s no-op-on-unchanged replay safety (webhook
/// delivery is at-least-once).
/// </summary>
public sealed class ProcessPayoutStatusCommandHandler(
    IRedeemRequestRepository redeemRequests,
    LedgerPostingService ledgerPostingService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<ProcessPayoutStatusResult> HandleAsync(
        ProcessPayoutStatusCommand command, CancellationToken cancellationToken = default)
    {
        var redeemRequest = await redeemRequests.FindByCircleRedeemIdAsync(command.CircleRedeemId, cancellationToken)
            ?? throw new NotFoundException(
                $"No redemption found for Circle redeem '{command.CircleRedeemId}'.");

        var mappedStatus = TransferStatusMapper.Map(command.Status);

        // Redelivered/replayed status events are a no-op once already applied — webhook
        // delivery is at-least-once, so replays must not double-save or double-debit.
        if (redeemRequest.Status == mappedStatus)
        {
            return new ProcessPayoutStatusResult(redeemRequest.Id, redeemRequest.Status);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (mappedStatus == TransferStatus.Complete)
        {
            var netAmount = command.NetAmount
                ?? throw new ArgumentException(
                    "NetAmount is required to settle a completed redemption.", nameof(command));
            var fees = command.Fees ?? Money.Zero(redeemRequest.GrossAmount.CurrencyCode);

            redeemRequest.Settle(fees, netAmount, nowUtc);

            // Debit uses the reserved GrossAmount from creation time, not the webhook's net
            // amount — the fee/net split already happened at the provider, the ledger only
            // needs to know how much left the sub-account's reserved balance.
            var debitAmount = redeemRequest.GrossAmount with
            {
                Amount = -Math.Abs(redeemRequest.GrossAmount.Amount),
            };
            var posting = new LedgerPosting(
                redeemRequest.SubAccountId,
                redeemRequest.ClientCompanyId,
                TransactionType.Redemption,
                debitAmount,
                redeemRequest.CircleRedeemId ?? command.CircleRedeemId,
                null,
                redeemRequest.CorrelationId);

            await ledgerPostingService.PostAsync(posting, cancellationToken);
        }
        else
        {
            var failureReason = mappedStatus == TransferStatus.Failed
                ? "Failed per Circle payout status webhook."
                : null;

            redeemRequest.UpdateStatus(mappedStatus, failureReason, nowUtc);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ProcessPayoutStatusResult(redeemRequest.Id, redeemRequest.Status);
    }
}
