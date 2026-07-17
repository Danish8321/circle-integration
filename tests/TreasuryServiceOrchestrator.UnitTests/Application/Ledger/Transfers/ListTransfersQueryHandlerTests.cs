using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
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
            .Setup(x => x.ListBySubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([transfer]);

        var result = await handler.HandleAsync(
            new ListTransfersQuery(subAccountId), TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(transfer.Id);
        result[0].SubAccountId.Should().Be(subAccountId);
    }
}
