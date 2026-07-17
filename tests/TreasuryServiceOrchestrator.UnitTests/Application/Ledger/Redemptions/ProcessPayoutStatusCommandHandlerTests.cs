using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Redemptions;

public sealed class ProcessPayoutStatusCommandHandlerTests
{
    private readonly Mock<IRedeemRequestRepository> redeemRequests = new();
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly Mock<IBalanceSnapshotRepository> balanceSnapshots = new();
    private readonly Mock<IFundAccountRepository> fundAccounts = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly LedgerPostingService ledgerPostingService;
    private readonly ProcessPayoutStatusCommandHandler handler;

    public ProcessPayoutStatusCommandHandlerTests()
    {
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundAccount?)null);

        ledgerPostingService = new LedgerPostingService(
            transactions.Object,
            balanceSnapshots.Object,
            fundAccounts.Object,
            unitOfWork.Object,
            TimeProvider.System);

        handler = new ProcessPayoutStatusCommandHandler(
            redeemRequests.Object, ledgerPostingService, unitOfWork.Object, TimeProvider.System);
    }

    private static RedeemRequest PendingRedeemRequest()
    {
        var redeemRequest = RedeemRequest.Create(
            Guid.NewGuid(), "client-1", Guid.NewGuid(), new Money(100m, "USDC"), "corr-1", DateTime.UtcNow);
        redeemRequest.SetProviderReference("circle-redeem-1", DateTime.UtcNow);
        return redeemRequest;
    }

    [Fact]
    public async Task HandleAsync_WithCompleteStatusAndExplicitToAmount_SettlesUsingSuppliedNetAmount()
    {
        var redeemRequest = PendingRedeemRequest();
        redeemRequests
            .Setup(x => x.FindByCircleRedeemIdAsync("circle-redeem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(redeemRequest);

        var result = await handler.HandleAsync(
            new ProcessPayoutStatusCommand(
                "circle-redeem-1", "complete", new Money(2m, "USDC"), new Money(98m, "USDC")),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Complete);
        redeemRequest.Fees!.Amount.Should().Be(2m);
        redeemRequest.NetAmount!.Amount.Should().Be(98m);
    }

    [Fact]
    public async Task HandleAsync_WithCompleteStatusAndCallerComputedNetAmount_SettlesUsingSuppliedValue()
    {
        // Mirrors the toAmount-absent branch: the webhook processor (07.5) computes
        // amount - fees before calling this command; this handler just uses whatever
        // NetAmount/Fees it is given, without re-deriving anything itself.
        var redeemRequest = PendingRedeemRequest();
        redeemRequests
            .Setup(x => x.FindByCircleRedeemIdAsync("circle-redeem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(redeemRequest);

        var result = await handler.HandleAsync(
            new ProcessPayoutStatusCommand(
                "circle-redeem-1", "complete", new Money(5m, "USDC"), new Money(95m, "USDC")),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Complete);
        redeemRequest.Fees!.Amount.Should().Be(5m);
        redeemRequest.NetAmount!.Amount.Should().Be(95m);
    }

    [Fact]
    public async Task HandleAsync_WithCompleteStatus_DebitsLedgerUsingGrossAmountNotNetAmount()
    {
        var redeemRequest = PendingRedeemRequest();
        redeemRequests
            .Setup(x => x.FindByCircleRedeemIdAsync("circle-redeem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(redeemRequest);

        await handler.HandleAsync(
            new ProcessPayoutStatusCommand(
                "circle-redeem-1", "complete", new Money(10m, "USDC"), new Money(90m, "USDC")),
            TestContext.Current.CancellationToken);

        transactions.Verify(
            x => x.AddAsync(
                It.Is<Transaction>(t =>
                    t.Type == TransactionType.Redemption && t.Amount.Amount == -100m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithCompleteStatusAndNoNetAmount_ThrowsArgumentException()
    {
        var redeemRequest = PendingRedeemRequest();
        redeemRequests
            .Setup(x => x.FindByCircleRedeemIdAsync("circle-redeem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(redeemRequest);

        var act = () => handler.HandleAsync(
            new ProcessPayoutStatusCommand("circle-redeem-1", "complete", null, null),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
        transactions.Verify(
            x => x.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithFailedStatus_UpdatesToFailedWithoutLedgerPosting()
    {
        var redeemRequest = PendingRedeemRequest();
        redeemRequests
            .Setup(x => x.FindByCircleRedeemIdAsync("circle-redeem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(redeemRequest);

        var result = await handler.HandleAsync(
            new ProcessPayoutStatusCommand("circle-redeem-1", "failed", null, null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Failed);
        redeemRequest.FailureReason.Should().NotBeNullOrEmpty();
        transactions.Verify(
            x => x.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithUnchangedStatus_IsNoOpAndDoesNotSave()
    {
        var redeemRequest = PendingRedeemRequest();
        redeemRequests
            .Setup(x => x.FindByCircleRedeemIdAsync("circle-redeem-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(redeemRequest);

        var result = await handler.HandleAsync(
            new ProcessPayoutStatusCommand("circle-redeem-1", "pending", null, null),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(TransferStatus.Pending);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownCircleRedeemId_ThrowsNotFound()
    {
        redeemRequests
            .Setup(x => x.FindByCircleRedeemIdAsync("unknown-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RedeemRequest?)null);

        var act = () => handler.HandleAsync(
            new ProcessPayoutStatusCommand("unknown-id", "complete", null, null),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
