using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Options;

using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;
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
    IMockRandomSource randomSource,
    IMockProviderDepositLedger depositLedger) : IStablecoinGateway
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
        RedeemGatewayRequest request, CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        var circleRedeemId = $"redeem-{randomSource.NewGuid():N}";

        // Pure 1:1, no fee simulation (ticket 07.6) — fees/net are computed only at the
        // payouts-webhook mapping edge, never here.
        var envelope = new PayoutsWebhookEnvelope
        {
            Payout = new PayoutsWebhookPayout
            {
                Id = circleRedeemId,
                Status = "complete",
                Amount = request.GrossAmount.Amount.ToString(CultureInfo.InvariantCulture),
                Fees = "0",
                ToAmount = request.GrossAmount.Amount.ToString(CultureInfo.InvariantCulture),
                Currency = request.GrossAmount.CurrencyCode,
            },
        };

        webhookScheduler.Schedule(
            "payouts",
            JsonSerializer.Serialize(envelope),
            TimeSpan.FromMilliseconds(options.Value.WebhookDelayMilliseconds));

        return Task.FromResult(new CreatedRedeem(circleRedeemId, "pending"));
    }

    public Task<CreatedLinkedBankAccount> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        var circleBankAccountId = $"bank-account-{randomSource.NewGuid():N}";

        var envelope = new WireWebhookEnvelope
        {
            Wire = new WireWebhookBankAccount
            {
                Id = circleBankAccountId,
                Status = "complete",
            },
        };

        webhookScheduler.Schedule(
            "wire",
            JsonSerializer.Serialize(envelope),
            TimeSpan.FromMilliseconds(options.Value.WebhookDelayMilliseconds));

        return Task.FromResult(new CreatedLinkedBankAccount(circleBankAccountId, "pending"));
    }

    public Task<WireInstructions> GetWireInstructionsAsync(
        string circleBankAccountId, CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        var trackingRef = $"MOCK{circleBankAccountId[..Math.Min(10, circleBankAccountId.Length)].ToUpperInvariant()}";

        return Task.FromResult(new WireInstructions(
            trackingRef,
            "Mock Beneficiary",
            "1 Mock Street, Mock City",
            "Mock Bank",
            "MOCKUS33",
            "021000021",
            "****1234",
            "USD"));
    }

    public Task<Money> GetMainWalletBalanceAsync(CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        return Task.FromResult(new Money(options.Value.MainWalletBalanceAmount, "USDC"));
    }

    public Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken ct = default)
    {
        MaybeThrowProviderUnavailable();

        return depositLedger.ListAsync(circleWalletId, sinceUtc, ct);
    }

    private void MaybeThrowProviderUnavailable()
    {
        if (randomSource.NextDouble() < options.Value.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated failure injection.");
        }
    }
}
