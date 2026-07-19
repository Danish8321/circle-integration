using System.Text.Json;
using FluentValidation;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Ledger;

/// <summary>
/// Posts an inbound deposit as a credit to the tenant's ledger. Deposits are triggered by a
/// provider event (webhook/reconciliation), not a caller-initiated retry-prone call, so the
/// idempotency key is scoped by ProviderReferenceId — the natural dedup key for a deposit
/// event. The "gateway/state-transition" step in the reserve -> gateway -> complete flow
/// (CLAUDE.md invariant 11) is the ledger post via <see cref="LedgerPostingService"/>, not an
/// outbound HTTP call.
/// </summary>
public sealed class ProcessDepositCommandHandler(
    LedgerPostingService ledgerPostingService,
    IIdempotencyService idempotency,
    IUnitOfWork unitOfWork,
    IValidator<ProcessDepositCommand> validator,
    ICallerContext callerContext)
    : ICommandHandler<ProcessDepositCommand, ProcessDepositResult>
{
    public async Task<ProcessDepositResult> HandleAsync(
        ProcessDepositCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var idempotencyKey = $"deposit:{command.ProviderReferenceId}";

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            callerContext.CallerId,
            idempotencyKey,
            command,
            unitOfWork,
            () => PostAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<ProcessDepositResult> PostAsync(
        ProcessDepositCommand command, CancellationToken cancellationToken)
    {
        var posting = new LedgerPosting(
            command.SubAccountId,
            callerContext.CallerId,
            TransactionType.Deposit,
            command.Amount with { Amount = Math.Abs(command.Amount.Amount) },
            command.ProviderReferenceId,
            command.DepositSourceType,
            command.CorrelationId);

        var transaction = await ledgerPostingService.PostAsync(
            posting, txn => BuildOutboxEntry(txn, command), deferCommit: true, ct: cancellationToken);

        return Map(transaction);
    }

    private static NotificationOutboxEntry BuildOutboxEntry(
        Transaction transaction, ProcessDepositCommand command) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = "DepositCredited",
            ClientCompanyId = transaction.ClientCompanyId,
            EntityId = transaction.Id.ToString(),
            OccurredAtUtc = transaction.UpdatedAtUtc,
            CorrelationId = command.CorrelationId,
            PayloadJson = JsonSerializer.Serialize(new { transaction.Amount, transaction.SubAccountId }),
            Status = NotificationDeliveryStatus.Pending,
            AttemptCount = 0,
            NextAttemptAtUtc = null,
            DeliveredAtUtc = null,
        };

    private static ProcessDepositResult Map(Transaction transaction) => new(
        transaction.Id,
        transaction.SubAccountId,
        transaction.Amount,
        transaction.Status,
        transaction.CreatedAtUtc);
}
