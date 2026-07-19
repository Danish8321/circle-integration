using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class ListAllTransactionsQueryHandlerTests
{
    private readonly Mock<ITransactionRepository> transactions = new();
    private readonly ListAllTransactionsQueryHandler handler;

    public ListAllTransactionsQueryHandlerTests()
    {
        handler = new ListAllTransactionsQueryHandler(transactions.Object);
    }

    [Fact]
    public async Task HandleAsync_PassesFilterToRepositoryAndReturnsResult()
    {
        var filter = new TransactionListFilter(ClientCompanyId: "client-1");
        var transaction = Transaction.Create(
            Guid.NewGuid(), "client-1", TransactionType.Deposit, TransactionStatus.Complete,
            new Money(10m, "USDC"), "provider-ref-1", DepositSourceType.OnChain, failureReason: null,
            "corr-1", DateTime.UtcNow);
        transactions
            .Setup(x => x.ListAllAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync([transaction]);

        var results = await handler.HandleAsync(
            new ListAllTransactionsQuery(filter), TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Should().Be(AdminTransactionResult.Map(transaction));
    }

    [Fact]
    public async Task HandleAsync_WithNoMatches_ReturnsEmptyList()
    {
        var filter = new TransactionListFilter();
        transactions
            .Setup(x => x.ListAllAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var results = await handler.HandleAsync(
            new ListAllTransactionsQuery(filter), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }
}
