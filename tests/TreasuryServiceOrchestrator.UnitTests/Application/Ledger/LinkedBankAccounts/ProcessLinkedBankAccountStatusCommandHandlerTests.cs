using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.LinkedBankAccounts;

public sealed class ProcessLinkedBankAccountStatusCommandHandlerTests
{
    private readonly Mock<ILinkedBankAccountRepository> linkedBankAccounts = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly ProcessLinkedBankAccountStatusCommandHandler handler;

    public ProcessLinkedBankAccountStatusCommandHandlerTests()
    {
        handler = new ProcessLinkedBankAccountStatusCommandHandler(
            linkedBankAccounts.Object, unitOfWork.Object, TimeProvider.System);
    }

    private static LinkedBankAccount PendingLinkedBankAccount()
    {
        var linkedBankAccount = LinkedBankAccount.Create(
            Guid.NewGuid(), "client-1", "Acme Inc", "12345678", "021000021", "Chase",
            "Acme Inc", "New York", "US", "123 Main St", "10001", null, "NY", "US", null, DateTime.UtcNow);
        linkedBankAccount.SetProviderReference("circle-bank-1", DateTime.UtcNow);
        return linkedBankAccount;
    }

    [Fact]
    public async Task HandleAsync_WithCompleteStatus_UpdatesToActive()
    {
        var linkedBankAccount = PendingLinkedBankAccount();
        linkedBankAccounts
            .Setup(x => x.FindByCircleBankAccountIdAsync("circle-bank-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);

        var result = await handler.HandleAsync(
            new ProcessLinkedBankAccountStatusCommand("circle-bank-1", "complete"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(LinkedBankAccountStatus.Active);
        linkedBankAccount.Status.Should().Be(LinkedBankAccountStatus.Active);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithFailedStatus_UpdatesToFailed()
    {
        var linkedBankAccount = PendingLinkedBankAccount();
        linkedBankAccounts
            .Setup(x => x.FindByCircleBankAccountIdAsync("circle-bank-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);

        var result = await handler.HandleAsync(
            new ProcessLinkedBankAccountStatusCommand("circle-bank-1", "failed"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(LinkedBankAccountStatus.Failed);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithUnchangedStatus_IsNoOpAndDoesNotSave()
    {
        var linkedBankAccount = PendingLinkedBankAccount();
        linkedBankAccounts
            .Setup(x => x.FindByCircleBankAccountIdAsync("circle-bank-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);

        var result = await handler.HandleAsync(
            new ProcessLinkedBankAccountStatusCommand("circle-bank-1", "pending"),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(LinkedBankAccountStatus.Pending);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnrecognizedStatusLiteral_Throws()
    {
        var linkedBankAccount = PendingLinkedBankAccount();
        linkedBankAccounts
            .Setup(x => x.FindByCircleBankAccountIdAsync("circle-bank-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);

        var act = () => handler.HandleAsync(
            new ProcessLinkedBankAccountStatusCommand("circle-bank-1", "some_future_literal"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownCircleBankAccountId_ThrowsNotFound()
    {
        linkedBankAccounts
            .Setup(x => x.FindByCircleBankAccountIdAsync("unknown-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LinkedBankAccount?)null);

        var act = () => handler.HandleAsync(
            new ProcessLinkedBankAccountStatusCommand("unknown-id", "complete"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
