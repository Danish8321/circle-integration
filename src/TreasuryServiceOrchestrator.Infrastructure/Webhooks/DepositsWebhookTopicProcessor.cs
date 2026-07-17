using System.Globalization;
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>
/// Ticket 04.6: processes the real Circle "deposits" SNS topic — fiat-wire deposits only
/// (docs/features/09-deposits-and-funding.md §3.5/§4). The payload carries a `walletId`, never a
/// destination address (that concept only exists on the `transfers` topic's on-chain path, owned
/// by 10-transfers.md), so the owning sub-account is resolved via
/// <see cref="ISubAccountRepository.GetByCircleWalletIdAsync"/> — not via
/// <c>IDepositAddressRepository.FindByAddressAsync</c>. That method is not added by this ticket:
/// per §3.4 step 2 of the feature doc, address-based resolution is exclusively the on-chain
/// (`transfers`-topic) path's concern, which this processor never sees.
/// </summary>
public sealed class DepositsWebhookTopicProcessor(
    ISubAccountRepository subAccountRepository,
    ICommandHandler<ProcessDepositCommand, ProcessDepositResult> processDepositHandler)
    : IWebhookTopicProcessor
{
    public string Topic => "deposits";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<DepositsWebhookEnvelope>(payloadJson)
            ?? throw new InvalidOperationException("Empty deposits webhook payload.");

        var deposit = envelope.Deposit
            ?? throw new InvalidOperationException("deposits webhook payload missing 'deposit'.");

        if (string.IsNullOrWhiteSpace(deposit.WalletId))
        {
            throw new DepositSourceNotResolvedException(
                "Unable to resolve deposit source: 'deposits' webhook payload has no walletId.");
        }

        var subAccount = await subAccountRepository.GetByCircleWalletIdAsync(deposit.WalletId, cancellationToken)
            ?? throw new DepositSourceNotResolvedException(
                $"Unable to resolve deposit source: no SubAccount found for CircleWalletId '{deposit.WalletId}'.");

        var command = new ProcessDepositCommand(
            subAccount.Id,
            new Money(decimal.Parse(deposit.Amount.Amount, CultureInfo.InvariantCulture), deposit.Amount.Currency),
            deposit.Id,
            DepositSourceType.Wire,
            deposit.Id);

        await processDepositHandler.HandleAsync(command, cancellationToken);
    }
}
