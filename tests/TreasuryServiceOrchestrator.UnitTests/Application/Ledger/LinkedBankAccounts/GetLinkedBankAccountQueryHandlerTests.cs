using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.LinkedBankAccounts;

public sealed class GetLinkedBankAccountQueryHandlerTests
{
    private readonly Mock<ILinkedBankAccountRepository> linkedBankAccounts = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetLinkedBankAccountQueryHandler handler;

    public GetLinkedBankAccountQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetLinkedBankAccountQueryHandler(linkedBankAccounts.Object, callerContext.Object);
    }

    [Fact]
    public async Task HandleAsync_WithKnownLinkedBankAccountId_ReturnsMappedResult()
    {
        var linkedBankAccount = LinkedBankAccount.Create(
            Guid.NewGuid(), "client-1", "Acme Inc", "12345678", "021000021", "Chase",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, "NY", "US", null, DateTime.UtcNow);
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(linkedBankAccount.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);

        var result = await handler.HandleAsync(
            new GetLinkedBankAccountQuery(linkedBankAccount.Id), TestContext.Current.CancellationToken);

        result.Id.Should().Be(linkedBankAccount.Id);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownLinkedBankAccountId_ThrowsNotFound()
    {
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LinkedBankAccount?)null);

        var act = () => handler.HandleAsync(
            new GetLinkedBankAccountQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
