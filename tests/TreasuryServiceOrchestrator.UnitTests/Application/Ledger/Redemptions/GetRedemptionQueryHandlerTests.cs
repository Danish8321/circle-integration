using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Redemptions;

public sealed class GetRedemptionQueryHandlerTests
{
    private readonly Mock<IRedeemRequestRepository> redeemRequests = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetRedemptionQueryHandler handler;

    public GetRedemptionQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetRedemptionQueryHandler(redeemRequests.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithKnownRedeemRequestId_ReturnsMappedResult()
    {
        var redeemRequest = RedeemRequest.Create(
            Guid.NewGuid(), "client-1", Guid.NewGuid(), new Money(50m, "USDC"), "corr-1", DateTime.UtcNow);
        redeemRequests
            .Setup(x => x.GetByIdAsync(redeemRequest.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(redeemRequest);

        var result = await handler.HandleAsync(
            new GetRedemptionQuery(redeemRequest.Id), TestContext.Current.CancellationToken);

        result.Id.Should().Be(redeemRequest.Id);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownRedeemRequestId_ThrowsNotFound()
    {
        redeemRequests
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RedeemRequest?)null);

        var act = () => handler.HandleAsync(
            new GetRedemptionQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
