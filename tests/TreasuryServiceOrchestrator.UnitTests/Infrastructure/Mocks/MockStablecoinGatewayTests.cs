using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;
using TreasuryServiceOrchestrator.TestUtilities;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

/// <summary>
/// Ticket 03.5: <see cref="MockStablecoinGateway.GenerateDepositAddressAsync"/> coverage —
/// success path (deterministic address/id generation via <see cref="IMockRandomSource"/>) and
/// failure-injection path, matching <see cref="MockSubAccountGatewayTests"/>'s conventions.
/// Ticket 05.5 adds <see cref="MockStablecoinGateway.RegisterRecipientAsync"/> coverage.
/// </summary>
public sealed class MockStablecoinGatewayTests
{
    private static GenerateDepositAddressGatewayRequest CreateRequest() =>
        new(Chain: "ETH", Currency: "USDC", IdempotencyKey: "deposit-address:sub-1:ETH:USDC");

    private static RegisterRecipientGatewayRequest CreateRecipientRequest(string label = "Primary payout wallet") =>
        new(Chain: "ETH", Address: "0xabc123", Label: label, IdempotencyKey: "recipient:sub-1:0xabc123");

    private static MockStablecoinGateway CreateSut(
        double failureInjectionRate = 0.0,
        IMockRandomSource? randomSource = null,
        CapturingScheduler? scheduler = null,
        string rejectSuffix = "REJECTME",
        int webhookDelayMilliseconds = 200)
    {
        var options = Options.Create(new MockProviderOptions
        {
            FailureInjectionRate = failureInjectionRate,
            RejectBusinessNameSuffix = rejectSuffix,
            WebhookDelayMilliseconds = webhookDelayMilliseconds,
        });

        return new MockStablecoinGateway(
            options,
            scheduler ?? new CapturingScheduler(),
            randomSource ?? new FixedRandomSource(fixedDouble: failureInjectionRate > 0 ? 0.0 : 0.5));
    }

    [Fact]
    public void MockStablecoinGateway_ImplementsIStablecoinGateway()
    {
        var sut = CreateSut();

        sut.Should().BeAssignableTo<IStablecoinGateway>();
    }

    [Fact]
    public async Task GenerateDepositAddressAsync_FailureInjectionRateZero_ReturnsAddressMatchingRequest()
    {
        var fixedGuid = new Guid("00000000-0000-0000-0000-0000000000ab");
        var sut = CreateSut(randomSource: new FixedRandomSource(fixedDouble: 0.5, fixedGuid: fixedGuid));

        var result = await sut.GenerateDepositAddressAsync(CreateRequest(), TestContext.Current.CancellationToken);

        result.Chain.Should().Be("ETH");
        result.Currency.Should().Be("USDC");
        result.Address.Should().Be($"0x{fixedGuid:N}");
        result.ProviderAddressId.Should().Be($"mock-addr-{fixedGuid}");
    }

    [Fact]
    public async Task GenerateDepositAddressAsync_FailureInjectionRateOne_ThrowsProviderUnavailable()
    {
        var sut = CreateSut(failureInjectionRate: 1.0);

        var act = () => sut.GenerateDepositAddressAsync(CreateRequest(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProviderUnavailableException>();
    }

    [Fact]
    public async Task RegisterRecipientAsync_FailureInjectionRateZero_ReturnsPendingVerificationWithDeterministicId()
    {
        var fixedGuid = new Guid("00000000-0000-0000-0000-0000000000ab");
        var sut = CreateSut(randomSource: new FixedRandomSource(fixedDouble: 0.5, fixedGuid: fixedGuid));

        var result = await sut.RegisterRecipientAsync(CreateRecipientRequest(), TestContext.Current.CancellationToken);

        result.CircleRecipientId.Should().Be($"mock-recipient-{fixedGuid}");
        result.Status.Should().Be("pending_verification");
    }

    [Fact]
    public async Task RegisterRecipientAsync_FailureInjectionRateOne_ThrowsProviderUnavailable()
    {
        var sut = CreateSut(failureInjectionRate: 1.0);

        var act = () => sut.RegisterRecipientAsync(CreateRecipientRequest(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProviderUnavailableException>();
    }

    [Fact]
    public async Task RegisterRecipientAsync_NormalLabel_SchedulesActiveAddressBookRecipientsWebhook()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler: scheduler);

        var result = await sut.RegisterRecipientAsync(CreateRecipientRequest(), TestContext.Current.CancellationToken);

        scheduler.Scheduled.Should().ContainSingle();
        var scheduled = scheduler.Scheduled[0];
        scheduled.Topic.Should().Be("addressBookRecipients");
        scheduled.Delay.Should().Be(TimeSpan.FromMilliseconds(200));

        var envelope = JsonSerializer.Deserialize<AddressBookRecipientsWebhookEnvelope>(scheduled.PayloadJson)!;
        envelope.AddressBookRecipient!.Id.Should().Be(result.CircleRecipientId);
        envelope.AddressBookRecipient.Status.Should().Be("active");
    }

    [Fact]
    public async Task RegisterRecipientAsync_MagicSuffixLabel_SchedulesDeniedAddressBookRecipientsWebhook()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler: scheduler);

        await sut.RegisterRecipientAsync(
            CreateRecipientRequest(label: "Primary payout wallet REJECTME"), TestContext.Current.CancellationToken);

        var scheduled = scheduler.Scheduled[0];
        var envelope = JsonSerializer.Deserialize<AddressBookRecipientsWebhookEnvelope>(scheduled.PayloadJson)!;
        envelope.AddressBookRecipient!.Status.Should().Be("denied");
    }
}
