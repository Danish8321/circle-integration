using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class GetTransactionQueryHandlerTests
{
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetTransactionQueryHandler handler;

    public GetTransactionQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetTransactionQueryHandler(transactions.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithExistingTransaction_ReturnsMappedResult()
    {
        var transaction = Transaction.Create(
            Guid.NewGuid(), "client-1", TransactionType.Deposit, TransactionStatus.Complete,
            new Money(10m, "USDC"), "provider-ref-1", DepositSourceType.OnChain, failureReason: null,
            "corr-1", DateTime.UtcNow);
        transactions
            .Setup(x => x.GetByIdAsync(transaction.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        var result = await handler.HandleAsync(
            new GetTransactionQuery(transaction.Id), TestContext.Current.CancellationToken);

        result.TransactionId.Should().Be(transaction.Id);
        result.Amount.Should().Be(new Money(10m, "USDC"));
    }

    [Fact]
    public async Task HandleAsync_WithMissingOrCrossTenantTransaction_ThrowsNotFound()
    {
        var transactionId = Guid.NewGuid();
        transactions
            .Setup(x => x.GetByIdAsync(transactionId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null);

        var act = () => handler.HandleAsync(
            new GetTransactionQuery(transactionId), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnidentifiedCaller_ThrowsTenantForbiddenWithoutQueryingRepository()
    {
        callerContext.Setup(x => x.CallerId).Returns(string.Empty);

        var act = () => handler.HandleAsync(
            new GetTransactionQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        transactions.Verify(
            x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
