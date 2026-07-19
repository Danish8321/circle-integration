using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Transfers;

public sealed class GetTransferQueryHandlerTests
{
    private readonly Mock<ITransferRepository> transfers = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetTransferQueryHandler handler;

    public GetTransferQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetTransferQueryHandler(transfers.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithKnownTransferId_ReturnsMappedResult()
    {
        var transfer = Transfer.Create(
            Guid.NewGuid(), "client-1", Guid.NewGuid(), new Money(50m, "USDC"), "corr-1", DateTime.UtcNow);
        transfers
            .Setup(x => x.GetByIdAsync(transfer.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transfer);

        var result = await handler.HandleAsync(
            new GetTransferQuery(transfer.Id), TestContext.Current.CancellationToken);

        result.Id.Should().Be(transfer.Id);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownTransferId_ThrowsNotFound()
    {
        transfers
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transfer?)null);

        var act = () => handler.HandleAsync(
            new GetTransferQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
