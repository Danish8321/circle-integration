using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.DepositAddresses;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.DepositAddresses;

public sealed class ListDepositAddressesQueryHandlerTests
{
    private readonly Mock<IDepositAddressRepository> depositAddresses = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ListDepositAddressesQueryHandler handler;

    public ListDepositAddressesQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new ListDepositAddressesQueryHandler(depositAddresses.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithAddressesForSubAccount_ReturnsMappedResults()
    {
        var subAccountId = Guid.NewGuid();
        var address = DepositAddress.Create(
            subAccountId, "ETH", "USDC", "0xabc", "circle-addr-1", DateTime.UtcNow);
        depositAddresses
            .Setup(x => x.ListForSubAccountAsync(subAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([address]);

        var results = await handler.HandleAsync(
            new ListDepositAddressesQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        results[0].Id.Should().Be(address.Id);
        results[0].SubAccountId.Should().Be(subAccountId);
        results[0].Address.Should().Be("0xabc");
        results[0].Chain.Should().Be("ETH");
        results[0].Currency.Should().Be("USDC");
    }

    [Fact]
    public async Task HandleAsync_WithNoUnidentifiedCaller_ThrowsTenantForbiddenWithoutQueryingRepository()
    {
        callerContext.Setup(x => x.CallerId).Returns(string.Empty);

        var act = () => handler.HandleAsync(
            new ListDepositAddressesQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        depositAddresses.Verify(
            x => x.ListForSubAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNoAddresses_ReturnsEmptyList()
    {
        var subAccountId = Guid.NewGuid();
        depositAddresses
            .Setup(x => x.ListForSubAccountAsync(subAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            new ListDepositAddressesQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }
}
