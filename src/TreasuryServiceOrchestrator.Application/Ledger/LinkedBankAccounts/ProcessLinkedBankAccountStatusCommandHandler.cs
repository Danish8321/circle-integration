using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

/// <summary>
/// Webhook-driven: updates a <see cref="Domain.LinkedBankAccount"/>'s status from a raw provider
/// status literal (<c>wire</c> topic, used by <c>WireWebhookTopicProcessor</c> — 07.5). Mirrors
/// <see cref="Transfers.ProcessTransferStatusCommandHandler"/>'s no-op-on-unchanged replay
/// safety (webhook delivery is at-least-once).
/// </summary>
public sealed class ProcessLinkedBankAccountStatusCommandHandler(
    ILinkedBankAccountRepository linkedBankAccounts,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<ProcessLinkedBankAccountStatusCommand, ProcessLinkedBankAccountStatusResult>
{
    public async Task<ProcessLinkedBankAccountStatusResult> HandleAsync(
        ProcessLinkedBankAccountStatusCommand command, CancellationToken cancellationToken = default)
    {
        var linkedBankAccount = await linkedBankAccounts.FindByCircleBankAccountIdAsync(
            command.CircleBankAccountId, cancellationToken)
            ?? throw new NotFoundException(
                $"No linked bank account found for Circle bank account '{command.CircleBankAccountId}'.");

        var mappedStatus = LinkedBankAccountStatusMapper.Map(command.Status);

        // Redelivered/replayed status events are a no-op once already applied — webhook
        // delivery is at-least-once, so replays must not double-save.
        if (linkedBankAccount.Status == mappedStatus)
        {
            return new ProcessLinkedBankAccountStatusResult(linkedBankAccount.Id, linkedBankAccount.Status);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        linkedBankAccount.UpdateStatus(mappedStatus, nowUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessLinkedBankAccountStatusResult(linkedBankAccount.Id, linkedBankAccount.Status);
    }
}
