using FluentAssertions;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.TestUtilities;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

/// <summary>
/// Ticket 03.5: <see cref="MockStablecoinGateway.GenerateDepositAddressAsync"/> coverage —
/// success path (deterministic address/id generation via <see cref="IMockRandomSource"/>) and
/// failure-injection path, matching <see cref="MockSubAccountGatewayTests"/>'s conventions.
/// </summary>
public sealed class MockStablecoinGatewayTests
{
    private static GenerateDepositAddressGatewayRequest CreateRequest() =>
        new(Chain: "ETH", Currency: "USDC", IdempotencyKey: "deposit-address:sub-1:ETH:USDC");

    private static MockStablecoinGateway CreateSut(
        double failureInjectionRate = 0.0, IMockRandomSource? randomSource = null)
    {
        var options = Options.Create(new MockProviderOptions
        {
            FailureInjectionRate = failureInjectionRate,
        });

        return new MockStablecoinGateway(
            options,
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
}
