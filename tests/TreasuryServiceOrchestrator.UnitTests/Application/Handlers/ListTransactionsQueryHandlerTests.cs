using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class ListTransactionsQueryHandlerTests
{
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ListTransactionsQueryHandler handler;

    public ListTransactionsQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new ListTransactionsQueryHandler(transactions.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithTransactionsForSubAccount_ReturnsMappedResults()
    {
        var subAccountId = Guid.NewGuid();
        var transaction = Transaction.Create(
            subAccountId, "client-1", TransactionType.Deposit, TransactionStatus.Complete,
            new Money(10m, "USDC"), "provider-ref-1", DepositSourceType.OnChain, failureReason: null,
            "corr-1", DateTime.UtcNow);
        transactions
            .Setup(x => x.ListBySubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([transaction]);

        var results = await handler.HandleAsync(
            new ListTransactionsQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().HaveCount(1);
        results[0].TransactionId.Should().Be(transaction.Id);
        results[0].SubAccountId.Should().Be(subAccountId);
        results[0].Amount.Should().Be(new Money(10m, "USDC"));
        results[0].Status.Should().Be(TransactionStatus.Complete);
    }

    [Fact]
    public async Task HandleAsync_WithUnidentifiedCaller_ThrowsTenantForbiddenWithoutQueryingRepository()
    {
        callerContext.Setup(x => x.CallerId).Returns(string.Empty);

        var act = () => handler.HandleAsync(
            new ListTransactionsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        transactions.Verify(
            x => x.ListBySubAccountAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithNoTransactions_ReturnsEmptyList()
    {
        var subAccountId = Guid.NewGuid();
        transactions
            .Setup(x => x.ListBySubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            new ListTransactionsQuery(subAccountId), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }
}
