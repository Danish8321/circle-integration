using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Transfers;

/// <summary>
/// Webhook-driven: updates a <see cref="Transfer"/>'s status from a raw provider status literal.
/// The ledger debit already happened at transfer-creation time
/// (<see cref="CreateTransferCommandHandler"/>), so this handler only updates the <see
/// cref="Transfer"/> row itself — no further ledger posting here. Mirrors
/// <c>ProcessRecipientDecisionHandler</c>'s no-op-on-unchanged replay safety (webhook delivery is
/// at-least-once).
/// </summary>
public sealed class ProcessTransferStatusCommandHandler(
    ITransferRepository transfers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>
{
    public async Task<ProcessTransferStatusResult> HandleAsync(
        ProcessTransferStatusCommand command, CancellationToken cancellationToken = default)
    {
        var transfer = await transfers.FindByCircleTransferIdAsync(command.CircleTransferId, cancellationToken)
            ?? throw new NotFoundException(
                $"No transfer found for Circle transfer '{command.CircleTransferId}'.");

        var mappedStatus = TransferStatusMapper.Map(command.Status);

        // Redelivered/replayed status events are a no-op once already applied — webhook
        // delivery is at-least-once, so replays must not double-save.
        if (transfer.Status == mappedStatus)
        {
            return new ProcessTransferStatusResult(transfer.Id, transfer.Status);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var failureReason = mappedStatus == TransferStatus.Failed
            ? "Failed per Circle transfer status webhook."
            : null;

        transfer.UpdateStatus(mappedStatus, failureReason, nowUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessTransferStatusResult(transfer.Id, transfer.Status);
    }
}
