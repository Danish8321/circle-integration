using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.LinkedBankAccounts;

public sealed class GetWireInstructionsQueryHandlerTests
{
    private readonly Mock<ILinkedBankAccountRepository> linkedBankAccounts = new();
    private readonly Mock<IStablecoinGateway> gateway = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly GetWireInstructionsQueryHandler handler;

    public GetWireInstructionsQueryHandlerTests()
    {
        callerContext.Setup(x => x.CallerId).Returns("client-1");
        handler = new GetWireInstructionsQueryHandler(
            linkedBankAccounts.Object, gateway.Object, callerContext.Object);
    }

    private static LinkedBankAccount LinkedAccountWithProviderReference()
    {
        var linkedBankAccount = LinkedBankAccount.Create(
            Guid.NewGuid(), "client-1", "Acme Inc", "12345678", "021000021", "Chase",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, "NY", "US", null, DateTime.UtcNow);
        linkedBankAccount.SetProviderReference("circle-bank-1", DateTime.UtcNow);
        return linkedBankAccount;
    }

    [Fact]
    public async Task HandleAsync_WithKnownLinkedBankAccount_ReturnsWireInstructionsFromGateway()
    {
        var linkedBankAccount = LinkedAccountWithProviderReference();
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(linkedBankAccount.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);
        var wireInstructions = new WireInstructions(
            "track-1", "Acme Inc", "123 Main St", "Chase", "CHASUS33", "021000021", "****5678", "USD");
        gateway
            .Setup(x => x.GetWireInstructionsAsync("circle-bank-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(wireInstructions);

        var result = await handler.HandleAsync(
            new GetWireInstructionsQuery(linkedBankAccount.Id), TestContext.Current.CancellationToken);

        result.Should().Be(wireInstructions);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownLinkedBankAccountId_ThrowsNotFound()
    {
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LinkedBankAccount?)null);

        var act = () => handler.HandleAsync(
            new GetWireInstructionsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithNoProviderReferenceYet_ThrowsConflict()
    {
        var linkedBankAccount = LinkedBankAccount.Create(
            Guid.NewGuid(), "client-1", "Acme Inc", "12345678", "021000021", "Chase",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, "NY", "US", null, DateTime.UtcNow);
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(linkedBankAccount.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);

        var act = () => handler.HandleAsync(
            new GetWireInstructionsQuery(linkedBankAccount.Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConflictException>();
    }
}
