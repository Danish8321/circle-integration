using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class CreateTransferCommandHandlerTests
{
    private readonly Mock<IRecipientRepository> recipients = new();
    private readonly Mock<ITransferRepository> transfers = new();
    private readonly Mock<IStablecoinGateway> gateway = new();
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly Mock<IBalanceSnapshotRepository> balanceSnapshots = new();
    private readonly Mock<IFundAccountRepository> fundAccounts = new();
    private readonly Mock<INotificationOutboxRepository> outbox = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IIdempotencyService> idempotency = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly LedgerPostingService ledgerPostingService;
    private readonly CreateTransferCommandHandler handler;

    public CreateTransferCommandHandlerTests()
    {
        idempotency
            .Setup(x => x.TryBeginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Started());
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundAccount?)null);

        ledgerPostingService = new LedgerPostingService(
            transactions.Object,
            balanceSnapshots.Object,
            fundAccounts.Object,
            outbox.Object,
            unitOfWork.Object,
            TimeProvider.System);

        handler = new CreateTransferCommandHandler(
            recipients.Object,
            transfers.Object,
            gateway.Object,
            ledgerPostingService,
            idempotency.Object,
            unitOfWork.Object,
            new CreateTransferCommandValidator(),
            TimeProvider.System,
            callerContext.Object);
    }

    private static Recipient ActiveRecipient() => Recipient.Create(
        Guid.NewGuid(), "client-1", "ETH", "0xabc", "My wallet", "circle-recipient-1",
        RecipientStatus.Active, DateTime.UtcNow);

    private static CreateTransferCommand ValidCommand(Guid? recipientId = null) => new(
        RecipientId: recipientId ?? Guid.NewGuid(),
        Amount: new Money(100m, "USDC"),
        IdempotencyKey: "idem-1",
        CorrelationId: "corr-1");

    [Fact]
    public async Task HandleAsync_WithActiveRecipient_ReservesIdempotencyKeyDebitsLedgerAndPersistsTransfer()
    {
        var recipient = ActiveRecipient();
        var command = ValidCommand(recipient.Id);
        recipients
            .Setup(x => x.FindByIdAsync(recipient.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);
        gateway
            .Setup(x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedTransfer("circle-transfer-1", "pending"));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.RecipientId.Should().Be(recipient.Id);
        result.Amount.Amount.Should().Be(100m);
        result.Status.Should().Be(TransferStatus.Pending);
        result.CircleTransferId.Should().Be("circle-transfer-1");

        idempotency.Verify(
            x => x.TryBeginAsync(
                "client-1", "idem-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // The ledger debit is a negative signed amount.
        transactions.Verify(
            x => x.AddAsync(
                It.Is<Transaction>(t => t.Type == TransactionType.Transfer && t.Amount.Amount == -100m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        transfers.Verify(x => x.AddAsync(It.IsAny<Transfer>(), It.IsAny<CancellationToken>()), Times.Once);

        // Two SaveChangesAsync calls: reserve (SaveChanges #1, the InProgress record before the
        // gateway) and complete (SaveChanges #2, the deferred ledger posting + Transfer aggregate +
        // idempotency completion committed atomically — ticket 23).
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_CommitsTheReservationBeforeCallingTheGateway()
    {
        // F6: the idempotency reservation must be durably persisted (SaveChanges #1) before the
        // money-moving provider call, so a crash after the gateway leaves a recoverable in-flight
        // record rather than a silent gap only Circle knows about.
        var recipient = ActiveRecipient();
        var command = ValidCommand(recipient.Id);
        recipients
            .Setup(x => x.FindByIdAsync(recipient.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);

        var order = new List<string>();
        unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save"))
            .Returns(Task.CompletedTask);
        gateway
            .Setup(x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("gateway"))
            .ReturnsAsync(new CreatedTransfer("circle-transfer-1", "pending"));

        await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        order.Should().Equal("save", "gateway", "save");
    }

    [Fact]
    public async Task HandleAsync_WithInFlightReservation_ReDrivesWorkWithoutASecondReserveSave()
    {
        // A prior attempt reserved but never completed (e.g. crashed after the gateway). The retry
        // re-drives: the reservation is already persisted, so no reserve SaveChanges — only the
        // completion commit. The gateway is re-called (Circle dedups on the idempotency key).
        var recipient = ActiveRecipient();
        var command = ValidCommand(recipient.Id);
        recipients
            .Setup(x => x.FindByIdAsync(recipient.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);
        idempotency
            .Setup(x => x.TryBeginAsync(
                "client-1", "idem-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.InFlightRetry());
        gateway
            .Setup(x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedTransfer("circle-transfer-1", "pending"));

        await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        gateway.Verify(
            x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        idempotency.Verify(
            x => x.CompleteAsync("client-1", "idem-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        // Only the completion commit — the InFlightRetry branch skips the reserve SaveChanges.
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownRecipient_ThrowsNotFoundWithoutCallingGateway()
    {
        var command = ValidCommand();
        recipients
            .Setup(x => x.FindByIdAsync(command.RecipientId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Recipient?)null);

        var act = () => handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
        gateway.Verify(
            x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithRecipientNotActive_ThrowsConflictWithoutCallingGateway()
    {
        var recipient = Recipient.Create(
            Guid.NewGuid(), "client-1", "ETH", "0xabc", "My wallet", "circle-recipient-1",
            RecipientStatus.PendingApproval, DateTime.UtcNow);
        var command = ValidCommand(recipient.Id);
        recipients
            .Setup(x => x.FindByIdAsync(recipient.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);

        var act = () => handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConflictException>();
        gateway.Verify(
            x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        transfers.Verify(x => x.AddAsync(It.IsAny<Transfer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenGatewayThrows_PropagatesWithoutPersistingTransfer()
    {
        var recipient = ActiveRecipient();
        var command = ValidCommand(recipient.Id);
        recipients
            .Setup(x => x.FindByIdAsync(recipient.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);
        gateway
            .Setup(x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderUnavailableException("Circle unavailable."));

        var act = () => handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ProviderUnavailableException>();
        transfers.Verify(x => x.AddAsync(It.IsAny<Transfer>(), It.IsAny<CancellationToken>()), Times.Never);
        transactions.Verify(x => x.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithCachedIdempotencyResult_ReturnsCachedResultWithoutCallingGatewayAgain()
    {
        var recipient = ActiveRecipient();
        var command = ValidCommand(recipient.Id);
        var cachedResult = new TransferResult(
            Guid.NewGuid(), recipient.SubAccountId, recipient.Id, command.Amount, "circle-transfer-cached",
            TransferStatus.Pending, null, DateTime.UtcNow);
        idempotency
            .Setup(x => x.TryBeginAsync(
                "client-1", "idem-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Replay(System.Text.Json.JsonSerializer.Serialize(cachedResult)));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.CircleTransferId.Should().Be("circle-transfer-cached");
        gateway.Verify(
            x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        recipients.Verify(
            x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        transfers.Verify(x => x.AddAsync(It.IsAny<Transfer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNonPositiveAmount_ThrowsValidationExceptionWithoutTouchingGateway()
    {
        var command = ValidCommand() with { Amount = new Money(0m, "USDC") };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        gateway.Verify(
            x => x.CreateTransferAsync(It.IsAny<CreateTransferGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
