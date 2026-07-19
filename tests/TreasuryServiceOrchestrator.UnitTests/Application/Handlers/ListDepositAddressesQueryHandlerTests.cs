using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

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
            .Setup(x => x.ListForSubAccountAsync(subAccountId, It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
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
            x => x.ListForSubAccountAsync(It.IsAny<Guid>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNoAddresses_ReturnsEmptyList()
    {
        var subAccountId = Guid.NewGuid();
        depositAddresses
            .Setup(x => x.ListForSubAccountAsync(subAccountId, It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            new ListDepositAddressesQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithPage2_ReturnsNextSliceNotDuplicateOfPage1AndPageSizeBoundsCount()
    {
        var subAccountId = Guid.NewGuid();
        var page1Address = DepositAddress.Create(
            subAccountId, "ETH", "USDC", "0xpage1", "circle-addr-page1", DateTime.UtcNow);
        var page2Address = DepositAddress.Create(
            subAccountId, "ETH", "USDC", "0xpage2", "circle-addr-page2", DateTime.UtcNow);

        depositAddresses
            .Setup(x => x.ListForSubAccountAsync(subAccountId, new PageRequest(1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page1Address]);
        depositAddresses
            .Setup(x => x.ListForSubAccountAsync(subAccountId, new PageRequest(2, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page2Address]);

        var page1Results = await handler.HandleAsync(
            new ListDepositAddressesQuery(subAccountId, new PageRequest(1, 1)),
            TestContext.Current.CancellationToken);
        var page2Results = await handler.HandleAsync(
            new ListDepositAddressesQuery(subAccountId, new PageRequest(2, 1)),
            TestContext.Current.CancellationToken);

        page1Results.Should().HaveCount(1);
        page2Results.Should().HaveCount(1);
        page1Results[0].Id.Should().Be(page1Address.Id);
        page2Results[0].Id.Should().Be(page2Address.Id);
        page2Results[0].Id.Should().NotBe(page1Results[0].Id);
    }
}
