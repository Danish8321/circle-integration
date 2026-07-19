using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.LinkedBankAccounts;

public sealed class ListLinkedBankAccountsQueryHandlerTests
{
    private readonly Mock<ILinkedBankAccountRepository> linkedBankAccounts = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ListLinkedBankAccountsQueryHandler handler;

    public ListLinkedBankAccountsQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new ListLinkedBankAccountsQueryHandler(linkedBankAccounts.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_ReturnsMappedLinkedBankAccountsForSubAccount()
    {
        var subAccountId = Guid.NewGuid();
        var linkedBankAccount = LinkedBankAccount.Create(
            subAccountId, "client-1", "Acme Inc", "12345678", "021000021", "Chase",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, "NY", "US", null, DateTime.UtcNow);
        linkedBankAccounts
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([linkedBankAccount]);

        var result = await handler.HandleAsync(
            new ListLinkedBankAccountsQuery(subAccountId), TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(linkedBankAccount.Id);
        result[0].SubAccountId.Should().Be(subAccountId);
    }

    [Fact]
    public async Task HandleAsync_WithPage2_ReturnsNextSliceNotDuplicateOfPage1AndPageSizeBoundsCount()
    {
        var subAccountId = Guid.NewGuid();
        var page1Account = LinkedBankAccount.Create(
            subAccountId, "client-1", "Acme Inc", "12345678", "021000021", "Chase",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, "NY", "US", null, DateTime.UtcNow);
        var page2Account = LinkedBankAccount.Create(
            subAccountId, "client-1", "Beta Inc", "87654321", "021000022", "Wells Fargo",
            "Beta Inc", "Boston", "US", "456 Main St", "02101", null, "MA", "US", null, DateTime.UtcNow);

        linkedBankAccounts
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", new PageRequest(1, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page1Account]);
        linkedBankAccounts
            .Setup(x => x.ListBySubAccountAsync(
                subAccountId, "client-1", new PageRequest(2, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync([page2Account]);

        var page1Results = await handler.HandleAsync(
            new ListLinkedBankAccountsQuery(subAccountId, new PageRequest(1, 1)),
            TestContext.Current.CancellationToken);
        var page2Results = await handler.HandleAsync(
            new ListLinkedBankAccountsQuery(subAccountId, new PageRequest(2, 1)),
            TestContext.Current.CancellationToken);

        page1Results.Should().HaveCount(1);
        page2Results.Should().HaveCount(1);
        page1Results[0].Id.Should().Be(page1Account.Id);
        page2Results[0].Id.Should().Be(page2Account.Id);
        page2Results[0].Id.Should().NotBe(page1Results[0].Id);
    }
}
