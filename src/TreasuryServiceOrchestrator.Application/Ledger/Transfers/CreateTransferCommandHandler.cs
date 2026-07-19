using FluentValidation;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Transfers;

/// <summary>
/// Creates an outbound on-chain transfer: reserve (idempotency), gateway/state-transition (call
/// the provider, then debit the ledger via <see cref="LedgerPostingService"/> with a negative
/// signed <see cref="Money"/> amount — no hand-rolled Transaction/FundAccount/BalanceSnapshot
/// triplet), complete (persist the <see cref="Transfer"/> row, idempotency wrap-up). Two
/// <c>SaveChangesAsync</c> calls total: one inside <see cref="LedgerPostingService.PostAsync"/>,
/// one inside <see cref="IdempotencyExecutor.ExecuteAsync{TResult}"/> after the result is cached
/// (CLAUDE.md invariant 11). The idempotency key is caller-supplied (invariant 11) — this is a
/// one-shot client request, not a webhook-driven local-dedup shape.
/// </summary>
public sealed class CreateTransferCommandHandler(
    IRecipientRepository recipients,
    ITransferRepository transfers,
    IStablecoinGateway gateway,
    LedgerPostingService ledgerPostingService,
    IIdempotencyService idempotency,
    IUnitOfWork unitOfWork,
    IValidator<CreateTransferCommand> validator,
    TimeProvider timeProvider,
    ICallerContext callerContext)
    : ICommandHandler<CreateTransferCommand, TransferResult>
{
    public async Task<TransferResult> HandleAsync(
        CreateTransferCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            callerContext.CallerId,
            command.IdempotencyKey,
            command,
            unitOfWork,
            () => CreateAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<TransferResult> CreateAsync(
        CreateTransferCommand command, CancellationToken cancellationToken)
    {
        var recipient = await recipients.FindByIdAsync(
            command.RecipientId, callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No recipient '{command.RecipientId}'.");

        // Destination must be an `active` recipient — structural enforcement of the two-stage
        // outbound workflow's rule (spec `10-outbound-transfers-and-recipients.md` §1.2).
        if (recipient.Status != RecipientStatus.Active)
        {
            throw new ConflictException(
                $"Recipient '{recipient.Id}' is not Active (current status: {recipient.Status}).");
        }

        var gatewayResult = await gateway.CreateTransferAsync(
            new CreateTransferGatewayRequest(
                recipient.CircleRecipientId ?? recipient.Address, command.Amount, command.IdempotencyKey),
            cancellationToken);

        var debitAmount = command.Amount with { Amount = -Math.Abs(command.Amount.Amount) };
        var posting = new LedgerPosting(
            recipient.SubAccountId,
            callerContext.CallerId,
            TransactionType.Transfer,
            debitAmount,
            gatewayResult.CircleTransferId,
            null,
            command.CorrelationId);

        await ledgerPostingService.PostAsync(
            posting, outboxEntryBuilder: null, deferCommit: true, ct: cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var transfer = Transfer.Create(
            recipient.SubAccountId,
            callerContext.CallerId,
            recipient.Id,
            command.Amount,
            command.CorrelationId,
            nowUtc);

        transfer.SetProviderReference(gatewayResult.CircleTransferId, nowUtc);

        var status = TransferStatusMapper.Map(gatewayResult.Status);
        if (status != TransferStatus.Pending)
        {
            transfer.UpdateStatus(status, null, nowUtc);
        }

        await transfers.AddAsync(transfer, cancellationToken);

        return Map(transfer);
    }

    private static TransferResult Map(Transfer transfer) => new(
        transfer.Id,
        transfer.SubAccountId,
        transfer.RecipientId,
        transfer.Amount,
        transfer.CircleTransferId,
        transfer.Status,
        transfer.FailureReason,
        transfer.CreatedAtUtc);
}
