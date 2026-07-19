using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class ListRedemptionsQueryHandlerTests
{
    private readonly Mock<IRedeemRequestRepository> redeemRequests = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ListRedemptionsQueryHandler handler;

    public ListRedemptionsQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new ListRedemptionsQueryHandler(redeemRequests.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_ReturnsMappedRedemptionsForSubAccount()
    {
        var subAccountId = Guid.NewGuid();
        var redeemRequest = RedeemRequest.Create(
            subAccountId, "client-1", Guid.NewGuid(), new Money(50m, "USDC"), "corr-1", DateTime.UtcNow);
        redeemRequests
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([redeemRequest]);

        var result = await handler.HandleAsync(
            new ListRedemptionsQuery(subAccountId), TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(redeemRequest.Id);
        result[0].SubAccountId.Should().Be(subAccountId);
    }

    [Fact]
    public async Task HandleAsync_WithPage2_ReturnsNextSliceNotDuplicateOfPage1AndPageSizeBoundsCount()
    {
        var subAccountId = Guid.NewGuid();
        var page1RedeemRequest = RedeemRequest.Create(
            subAccountId, "client-1", Guid.NewGuid(), new Money(10m, "USDC"), "corr-page1", DateTime.UtcNow);
        var page2RedeemRequest = RedeemRequest.Create(
            subAccountId, "client-1", Guid.NewGuid(), new Money(20m, "USDC"), "corr-page2", DateTime.UtcNow);

        redeemRequests
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", new PageRequest(1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page1RedeemRequest]);
        redeemRequests
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", new PageRequest(2, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page2RedeemRequest]);

        var page1Results = await handler.HandleAsync(
            new ListRedemptionsQuery(subAccountId, new PageRequest(1, 1)), TestContext.Current.CancellationToken);
        var page2Results = await handler.HandleAsync(
            new ListRedemptionsQuery(subAccountId, new PageRequest(2, 1)), TestContext.Current.CancellationToken);

        page1Results.Should().HaveCount(1);
        page2Results.Should().HaveCount(1);
        page1Results[0].Id.Should().Be(page1RedeemRequest.Id);
        page2Results[0].Id.Should().Be(page2RedeemRequest.Id);
        page2Results[0].Id.Should().NotBe(page1Results[0].Id);
    }
}
