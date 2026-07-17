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

    private void MaybeThrowProviderUnavailable()
    {
        if (randomSource.NextDouble() < options.Value.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated failure injection.");
        }
    }
}
