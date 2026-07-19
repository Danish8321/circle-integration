using System.Globalization;
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>
/// Ticket 06.4: processes the real Circle "transfers" SNS topic
/// (docs/features/10-outbound-transfers-and-recipients.md §3.5). Doc-grilling correction #5: this
/// topic is **shared** between two directions — an outgoing transfer this service initiated
/// (status-update branch, owned by this feature) and an incoming on-chain deposit arriving in one
/// of our wallets (credit branch, owned by 09-deposits-and-funding.md). Direction is discriminated
/// by whether a local <see cref="Transfer"/> row already exists for the payload's transfer id
/// (<see cref="ITransferRepository.FindByCircleTransferIdAsync"/>): a hit means this service
/// created the transfer (outgoing), a miss means it is an incoming on-chain credit.
/// </summary>
public sealed class TransfersWebhookTopicProcessor(
    ITransferRepository transferRepository,
    ISubAccountRepository subAccountRepository,
    ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult> processTransferStatusHandler,
    ICommandHandler<ProcessDepositCommand, ProcessDepositResult> processDepositHandler,
    ISettableCallerContext callerContext)
    : IWebhookTopicProcessor
{
    public string Topic => "transfers";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<TransfersWebhookEnvelope>(payloadJson)
            ?? throw new InvalidOperationException("Empty transfers webhook payload.");

        var transfer = envelope.Transfer
            ?? throw new InvalidOperationException("transfers webhook payload missing 'transfer'.");

        if (string.IsNullOrWhiteSpace(transfer.Id) || string.IsNullOrWhiteSpace(transfer.Status))
        {
            throw new InvalidOperationException("transfers webhook payload missing 'id' or 'status'.");
        }

        var existingTransfer = await transferRepository.FindByCircleTransferIdAsync(transfer.Id, cancellationToken);

        if (existingTransfer is not null)
        {
            // Outgoing branch: a Transfer row already exists for this Circle transfer id, so this
            // is a status update for a transfer this service initiated. The raw status literal is
            // forwarded as-is — TransferStatusMapper (inside the handler) owns the
            // running -> Pending collapse, not this processor.
            await processTransferStatusHandler.HandleAsync(
                new ProcessTransferStatusCommand(transfer.Id, transfer.Status), cancellationToken);
            return;
        }

        // Incoming branch: no local Transfer row, so this is an on-chain deposit arriving in one
        // of our wallets. Resolve the owning SubAccount from destination.id (our wallet id) —
        // mirrors DepositsWebhookTopicProcessor's fiat-wire wallet-id resolution.
        var destinationWalletId = transfer.Destination?.Id;
        if (string.IsNullOrWhiteSpace(destinationWalletId))
        {
            throw new DepositSourceNotResolvedException(
                "Unable to resolve deposit source: 'transfers' webhook payload has no destination.id.");
        }

        var subAccount = await subAccountRepository.GetByCircleWalletIdAsync(destinationWalletId, cancellationToken)
            ?? throw new DepositSourceNotResolvedException(
                $"Unable to resolve deposit source: no SubAccount found for CircleWalletId '{destinationWalletId}'.");

        // The webhook route is authenticated by SNS signature verification, not the
        // ClientCompanyId header (CallerIdentityMiddleware bypasses it) — establish the resolved
        // tenant here so ProcessDepositCommandHandler's ICallerContext read (CLAUDE.md
        // invariant 7) resolves to the deposit's owning tenant.
        callerContext.Set(subAccount.ClientCompanyId, CallerRole.SubAccount);

        var command = new ProcessDepositCommand(
            subAccount.Id,
            new Money(decimal.Parse(transfer.Amount.Amount, CultureInfo.InvariantCulture), transfer.Amount.Currency),
            transfer.Id,
            DepositSourceType.OnChain,
            transfer.Id);

        await processDepositHandler.HandleAsync(command, cancellationToken);
    }
}
