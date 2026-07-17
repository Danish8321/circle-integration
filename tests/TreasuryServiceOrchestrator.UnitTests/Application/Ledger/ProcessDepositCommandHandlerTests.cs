using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger;

public sealed class ProcessDepositCommandHandlerTests
{
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly Mock<IBalanceSnapshotRepository> balanceSnapshots = new();
    private readonly Mock<IFundAccountRepository> fundAccounts = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IIdempotencyService> idempotency = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly LedgerPostingService ledgerPostingService;
    private readonly ProcessDepositCommandHandler handler;

    public ProcessDepositCommandHandlerTests()
    {
        idempotency
            .Setup(x => x.TryGetCachedResultJsonAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        fundAccounts
            .Setup(x => x.FindByClientCompanyIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundAccount?)null);

        ledgerPostingService = new LedgerPostingService(
            transactions.Object,
            balanceSnapshots.Object,
            fundAccounts.Object,
            unitOfWork.Object,
            TimeProvider.System);

        handler = new ProcessDepositCommandHandler(
            ledgerPostingService,
            idempotency.Object,
            unitOfWork.Object,
            new ProcessDepositCommandValidator(),
            callerContext.Object);
    }

    private static ProcessDepositCommand ValidCommand(string? providerReferenceId = null) => new(
        SubAccountId: Guid.NewGuid(),
        Amount: new Money(100m, "USDC"),
        ProviderReferenceId: providerReferenceId ?? $"provider-ref-{Guid.NewGuid()}",
        DepositSourceType: DepositSourceType.Wire,
        CorrelationId: "corr-1");

    [Fact]
    public async Task HandleAsync_WithNoCachedResult_ReservesIdempotencyKeyThenPostsCreditViaLedgerPostingService()
    {
        var command = ValidCommand();
        var expectedKey = $"deposit:{command.ProviderReferenceId}";

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.SubAccountId.Should().Be(command.SubAccountId);
        result.Amount.Amount.Should().Be(100m);
        result.Status.Should().Be(TransactionStatus.Complete);

        idempotency.Verify(
            x => x.TryGetCachedResultJsonAsync(
                "client-1", expectedKey, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        transactions.Verify(
            x => x.AddAsync(
                It.Is<Transaction>(t => t.Type == TransactionType.Deposit && t.Amount.Amount == 100m),
                It.IsAny<CancellationToken>()),
            Times.Once);
        idempotency.Verify(
            x => x.StoreResultAsync(
                "client-1", expectedKey, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Two SaveChangesAsync calls: one inside LedgerPostingService.PostAsync (state
        // transition), one inside IdempotencyExecutor after StoreResultAsync (reserve/complete).
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WithCachedIdempotencyResult_ReturnsCachedResultWithoutPostingAgain()
    {
        var command = ValidCommand();
        var expectedKey = $"deposit:{command.ProviderReferenceId}";
        var cachedResult = new ProcessDepositResult(
            Guid.NewGuid(), command.SubAccountId, command.Amount, TransactionStatus.Complete, DateTime.UtcNow);
        idempotency
            .Setup(x => x.TryGetCachedResultJsonAsync(
                "client-1", expectedKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Json.JsonSerializer.Serialize(cachedResult));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.TransactionId.Should().Be(cachedResult.TransactionId);
        transactions.Verify(
            x => x.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNegativeAmount_ThrowsValidationExceptionWithoutTouchingRepository()
    {
        var command = ValidCommand() with { Amount = new Money(-5m, "USDC") };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        transactions.Verify(
            x => x.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithEmptySubAccountId_ThrowsValidationException()
    {
        var command = ValidCommand() with { SubAccountId = Guid.Empty };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_WithEmptyProviderReferenceId_ThrowsValidationException()
    {
        var command = ValidCommand() with { ProviderReferenceId = string.Empty };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
