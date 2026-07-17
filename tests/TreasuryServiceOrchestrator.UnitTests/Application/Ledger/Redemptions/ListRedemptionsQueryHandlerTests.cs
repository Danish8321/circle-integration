using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Redemptions;

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
            .Setup(x => x.ListBySubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([redeemRequest]);

        var result = await handler.HandleAsync(
            new ListRedemptionsQuery(subAccountId), TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(redeemRequest.Id);
        result[0].SubAccountId.Should().Be(subAccountId);
    }
}
