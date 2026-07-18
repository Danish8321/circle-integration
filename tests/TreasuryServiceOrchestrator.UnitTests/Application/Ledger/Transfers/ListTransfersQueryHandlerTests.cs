using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Transfers;

public sealed class ListTransfersQueryHandlerTests
{
    private readonly Mock<ITransferRepository> transfers = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ListTransfersQueryHandler handler;

    public ListTransfersQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new ListTransfersQueryHandler(transfers.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_ReturnsMappedTransfersForSubAccount()
    {
        var subAccountId = Guid.NewGuid();
        var transfer = Transfer.Create(
            subAccountId, "client-1", Guid.NewGuid(), new Money(50m, "USDC"), "corr-1", DateTime.UtcNow);
        transfers
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([transfer]);

        var result = await handler.HandleAsync(
            new ListTransfersQuery(subAccountId), TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(transfer.Id);
        result[0].SubAccountId.Should().Be(subAccountId);
    }

    [Fact]
    public async Task HandleAsync_WithPage2_ReturnsNextSliceNotDuplicateOfPage1AndPageSizeBoundsCount()
    {
        var subAccountId = Guid.NewGuid();
        var page1Transfer = Transfer.Create(
            subAccountId, "client-1", Guid.NewGuid(), new Money(10m, "USDC"), "corr-page1", DateTime.UtcNow);
        var page2Transfer = Transfer.Create(
            subAccountId, "client-1", Guid.NewGuid(), new Money(20m, "USDC"), "corr-page2", DateTime.UtcNow);

        transfers
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", new PageRequest(1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page1Transfer]);
        transfers
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", new PageRequest(2, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page2Transfer]);

        var page1Results = await handler.HandleAsync(
            new ListTransfersQuery(subAccountId, new PageRequest(1, 1)), TestContext.Current.CancellationToken);
        var page2Results = await handler.HandleAsync(
            new ListTransfersQuery(subAccountId, new PageRequest(2, 1)), TestContext.Current.CancellationToken);

        page1Results.Should().HaveCount(1);
        page2Results.Should().HaveCount(1);
        page1Results[0].Id.Should().Be(page1Transfer.Id);
        page2Results[0].Id.Should().Be(page2Transfer.Id);
        page2Results[0].Id.Should().NotBe(page1Results[0].Id);
    }
}
