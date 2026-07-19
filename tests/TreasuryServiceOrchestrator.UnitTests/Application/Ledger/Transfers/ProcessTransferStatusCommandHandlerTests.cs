using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Transfers;

public sealed class ProcessTransferStatusCommandHandlerTests
{
    private readonly Mock<ITransferRepository> transfers = new();
    private readonly Mock<INotificationOutboxRepository> outbox = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly ProcessTransferStatusCommandHandler handler;

    public ProcessTransferStatusCommandHandlerTests()
    {
        handler = new ProcessTransferStatusCommandHandler(
            transfers.Object, outbox.Object, unitOfWork.Object, TimeProvider.System);
    }

    private static Transfer PendingTransfer()
    {
        var transfer = Transfer.Create(
            Guid.NewGuid(), "client-1", Guid.NewGuid(), new Money(100m, "USDC"), "corr-1", DateTime.UtcNow);
        transfer.SetProviderReference("circle-transfer-1", DateTime.UtcNow);
        return transfer;
    }

    [Fact]
    public async Task HandleAsync_WithCompleteStatus_UpdatesTransferToComplete()
    {
        var transfer = PendingTransfer();
        transfers
            .Setup(x => x.FindByCircleTransferIdAsync("circle-transfer-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transfer);

        var result = await handler.HandleAsync(
            new ProcessTransferStatusCommand("circle-transfer-1", "complete"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Complete);
        transfer.Status.Should().Be(TransferStatus.Complete);
        transfer.FailureReason.Should().BeNull();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(
            x => x.AddAsync(
                It.Is<NotificationOutboxEntry>(e => e.EventType == "TransferCompleted"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithFailedStatus_UpdatesTransferToFailedWithReason()
    {
        var transfer = PendingTransfer();
        transfers
            .Setup(x => x.FindByCircleTransferIdAsync("circle-transfer-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transfer);

        var result = await handler.HandleAsync(
            new ProcessTransferStatusCommand("circle-transfer-1", "failed"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Failed);
        transfer.Status.Should().Be(TransferStatus.Failed);
        transfer.FailureReason.Should().NotBeNullOrEmpty();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithRunningStatus_MapsToNoOpPendingAndDoesNotSave()
    {
        var transfer = PendingTransfer();
        transfers
            .Setup(x => x.FindByCircleTransferIdAsync("circle-transfer-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transfer);

        var result = await handler.HandleAsync(
            new ProcessTransferStatusCommand("circle-transfer-1", "running"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Pending);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnchangedStatus_IsNoOpAndDoesNotSave()
    {
        var transfer = PendingTransfer();
        transfers
            .Setup(x => x.FindByCircleTransferIdAsync("circle-transfer-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transfer);

        var result = await handler.HandleAsync(
            new ProcessTransferStatusCommand("circle-transfer-1", "pending"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Pending);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownCircleTransferId_ThrowsNotFound()
    {
        transfers
            .Setup(x => x.FindByCircleTransferIdAsync("unknown-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transfer?)null);

        var act = () => handler.HandleAsync(
            new ProcessTransferStatusCommand("unknown-id", "complete"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
