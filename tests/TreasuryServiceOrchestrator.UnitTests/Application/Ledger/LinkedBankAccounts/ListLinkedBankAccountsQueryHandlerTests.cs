using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
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
            .Setup(x => x.ListBySubAccountAsync(subAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([linkedBankAccount]);

        var result = await handler.HandleAsync(
            new ListLinkedBankAccountsQuery(subAccountId), TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(linkedBankAccount.Id);
        result[0].SubAccountId.Should().Be(subAccountId);
    }
}
