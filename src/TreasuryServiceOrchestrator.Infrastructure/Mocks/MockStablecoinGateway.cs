using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Options;

using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of <see cref="IStablecoinGateway"/> (docs/features/02-mock-mode.md §3.4).
/// Ticket 03.5 extends it with <see cref="GenerateDepositAddressAsync"/>; later tickets
/// (05/06/07) extend it further alongside their corresponding <see cref="IStablecoinGateway"/>
/// additions. Holds no state as of this ticket (deposit addresses are permanent per (chain,
/// currency) and dedup is owned by the Application-tier repository, not the gateway), so no
/// singleton lifetime requirement yet — matches <see cref="MockSubAccountGateway"/>'s
/// registration convention regardless.
/// </summary>
public sealed class MockStablecoinGateway(
    IOptions<MockProviderOptions> options,
    IMockWebhookScheduler webhookScheduler,
    IMockRandomSource randomSource) : IStablecoinGateway
{
    public Task<GeneratedDepositAddress> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        var address = $"0x{randomSource.NewGuid():N}";

        return Task.FromResult(new GeneratedDepositAddress(
            address, request.Chain, request.Currency, $"mock-addr-{randomSource.NewGuid()}"));
    }

    public Task<RegisteredRecipient> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        var circleRecipientId = $"mock-recipient-{randomSource.NewGuid()}";
        var webhookStatus = request.Label.EndsWith(
            options.Value.RejectBusinessNameSuffix, StringComparison.OrdinalIgnoreCase)
            ? "denied"
            : "active";

        var envelope = new AddressBookRecipientsWebhookEnvelope
        {
            AddressBookRecipient = new AddressBookRecipientsWebhookRecipient
            {
                Id = circleRecipientId,
                Status = webhookStatus,
            },
        };

        webhookScheduler.Schedule(
            "addressBookRecipients",
            JsonSerializer.Serialize(envelope),
            TimeSpan.FromMilliseconds(options.Value.WebhookDelayMilliseconds));

        return Task.FromResult(new RegisteredRecipient(circleRecipientId, "pending_verification"));
    }

    public Task<CreatedTransfer> CreateTransferAsync(
        CreateTransferGatewayRequest request, CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        var circleTransferId = $"mock-transfer-{randomSource.NewGuid()}";
        var isRejected = request.DestinationRecipientId.EndsWith(
            options.Value.RejectBusinessNameSuffix, StringComparison.OrdinalIgnoreCase);

        var runningEnvelope = new TransfersWebhookEnvelope
        {
            Transfer = new TransfersWebhookTransfer
            {
                Id = circleTransferId,
                Status = "running",
                Destination = new TransfersWebhookParty { Type = "verified_blockchain", Id = request.DestinationRecipientId },
                Amount = new TransfersWebhookAmount
                {
                    Amount = request.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                    Currency = request.Amount.CurrencyCode,
                },
            },
        };

        var outcomeEnvelope = new TransfersWebhookEnvelope
        {
            Transfer = new TransfersWebhookTransfer
            {
                Id = circleTransferId,
                Status = isRejected ? "failed" : "complete",
                Destination = new TransfersWebhookParty { Type = "verified_blockchain", Id = request.DestinationRecipientId },
                Amount = new TransfersWebhookAmount
                {
                    Amount = request.Amount.Amount.ToString(CultureInfo.InvariantCulture),
                    Currency = request.Amount.CurrencyCode,
                },
            },
        };

        var delay = TimeSpan.FromMilliseconds(options.Value.WebhookDelayMilliseconds);
        webhookScheduler.Schedule("transfers", JsonSerializer.Serialize(runningEnvelope), delay);
        webhookScheduler.Schedule("transfers", JsonSerializer.Serialize(outcomeEnvelope), delay + delay);

        return Task.FromResult(new CreatedTransfer(circleTransferId, "pending"));
    }

    public Task<CreatedRedeem> RedeemAsync(
        RedeemGatewayRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "MockStablecoinGateway.RedeemAsync is implemented in ticket 07.6.");

    public Task<CreatedLinkedBankAccount> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "MockStablecoinGateway.CreateLinkedBankAccountAsync is implemented in ticket 07.6.");

    public Task<WireInstructions> GetWireInstructionsAsync(
        string circleBankAccountId, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "MockStablecoinGateway.GetWireInstructionsAsync is implemented in ticket 07.6.");

    private void MaybeThrowProviderUnavailable()
    {
        if (randomSource.NextDouble() < options.Value.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated failure injection.");
        }
    }
}
