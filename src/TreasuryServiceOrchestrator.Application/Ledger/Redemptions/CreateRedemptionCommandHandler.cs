using FluentValidation;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

/// <summary>
/// Creates a redemption: reserve (idempotency), gateway/state-transition (call the provider's
/// redeem endpoint with an always-explicit <c>SourceWalletId</c> — never omitted, per
/// <see cref="RedeemGatewayRequest"/>'s doc comment and CLAUDE.md invariant 12's hazard family),
/// complete (persist the <see cref="RedeemRequest"/> row). <c>GrossAmount</c> is validated against
/// the recipient <see cref="LinkedBankAccount"/> being <see cref="LinkedBankAccountStatus.Active"/>
/// and reserved with the provider here, but NOT debited from the ledger yet — the debit happens
/// later, on the webhook-driven Complete transition
/// (docs/features/11-redemption-and-payouts.md §4, see <see cref="ProcessPayoutStatusCommandHandler"/>).
/// Two <c>SaveChangesAsync</c> calls total: one inside <see cref="IUnitOfWork.SaveChangesAsync"/>
/// after <see cref="IIdempotencyService.StoreResultAsync"/> inside
/// <see cref="IdempotencyExecutor.ExecuteAsync{TResult}"/>, no ledger posting here at all
/// (CLAUDE.md invariant 11 — a reserve-then-later-debit shape, not reserve+debit-now).
/// </summary>
public sealed class CreateRedemptionCommandHandler(
    ILinkedBankAccountRepository linkedBankAccounts,
    ISubAccountRepository subAccounts,
    IRedeemRequestRepository redeemRequests,
    IStablecoinGateway gateway,
    IIdempotencyService idempotency,
    IUnitOfWork unitOfWork,
    IValidator<CreateRedemptionCommand> validator,
    TimeProvider timeProvider,
    ICallerContext callerContext)
    : ICommandHandler<CreateRedemptionCommand, RedemptionResult>
{
    public async Task<RedemptionResult> HandleAsync(
        CreateRedemptionCommand command, CancellationToken cancellationToken = default)
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

    private async Task<RedemptionResult> CreateAsync(
        CreateRedemptionCommand command, CancellationToken cancellationToken)
    {
        var linkedBankAccount = await linkedBankAccounts.GetByIdAsync(
            command.LinkedBankAccountId, callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No linked bank account '{command.LinkedBankAccountId}'.");

        // Destination must be an Active linked bank account — structural enforcement of the
        // reserve-at-creation rule (spec `11-redemption-and-payouts.md` §4).
        if (linkedBankAccount.Status != LinkedBankAccountStatus.Active)
        {
            throw new ConflictException(
                $"LinkedBankAccount '{linkedBankAccount.Id}' is not Active " +
                $"(current status: {linkedBankAccount.Status}).");
        }

        if (string.IsNullOrWhiteSpace(linkedBankAccount.CircleBankAccountId))
        {
            throw new ConflictException(
                $"LinkedBankAccount '{linkedBankAccount.Id}' has no provider reference.");
        }

        var subAccount = await subAccounts.GetByClientCompanyIdAsync(callerContext.CallerId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account for '{callerContext.CallerId}'.");

        if (string.IsNullOrWhiteSpace(subAccount.CircleWalletId))
        {
            throw new ConflictException($"SubAccount '{subAccount.Id}' has no provider wallet reference.");
        }

        // Source is always explicit — an omitted source would silently debit the Distributor's
        // Master Account wallet instead of the sub-account's (CLAUDE.md invariant 12 hazard family).
        var gatewayResult = await gateway.RedeemAsync(
            new RedeemGatewayRequest(
                command.IdempotencyKey,
                subAccount.CircleWalletId,
                linkedBankAccount.CircleBankAccountId,
                command.GrossAmount),
            cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var redeemRequest = RedeemRequest.Create(
            linkedBankAccount.SubAccountId,
            callerContext.CallerId,
            linkedBankAccount.Id,
            command.GrossAmount,
            command.CorrelationId,
            nowUtc);

        redeemRequest.SetProviderReference(gatewayResult.CircleRedeemId, nowUtc);

        var status = TransferStatusMapper.Map(gatewayResult.Status);
        if (status != TransferStatus.Pending)
        {
            redeemRequest.UpdateStatus(status, null, nowUtc);
        }

        await redeemRequests.AddAsync(redeemRequest, cancellationToken);

        return Map(redeemRequest);
    }

    private static RedemptionResult Map(RedeemRequest redeemRequest) => new(
        redeemRequest.Id,
        redeemRequest.SubAccountId,
        redeemRequest.LinkedBankAccountId,
        redeemRequest.GrossAmount,
        redeemRequest.Fees,
        redeemRequest.NetAmount,
        redeemRequest.CircleRedeemId,
        redeemRequest.Status,
        redeemRequest.FailureReason,
        redeemRequest.CreatedAtUtc);
}
