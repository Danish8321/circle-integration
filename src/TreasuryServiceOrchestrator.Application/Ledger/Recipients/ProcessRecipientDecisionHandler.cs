using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Recipients;

public sealed class ProcessRecipientDecisionHandler(
    IRecipientRepository recipients,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>
{
    public async Task<ProcessRecipientDecisionResult> HandleAsync(
        ProcessRecipientDecisionCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await recipients.FindByCircleRecipientIdAsync(command.CircleRecipientId, cancellationToken)
            ?? throw new NotFoundException(
                $"No recipient found for Circle recipient '{command.CircleRecipientId}'.");

        var mappedStatus = RecipientStatusMapper.Map(command.Status);

        // Redelivered/replayed decision events are a no-op once already applied — webhook
        // delivery is at-least-once, so replays must not double-save.
        if (recipient.Status == mappedStatus)
        {
            return new ProcessRecipientDecisionResult(recipient.Id, recipient.Status);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var denialReason = mappedStatus == RecipientStatus.Denied
            ? "Denied by Circle recipient approval decision."
            : null;

        recipient.UpdateStatus(mappedStatus, denialReason, nowUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessRecipientDecisionResult(recipient.Id, recipient.Status);
    }
}
