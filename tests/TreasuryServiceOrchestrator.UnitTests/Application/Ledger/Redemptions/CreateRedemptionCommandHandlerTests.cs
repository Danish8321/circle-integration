using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Redemptions;

public sealed class CreateRedemptionCommandHandlerTests
{
    private readonly Mock<ILinkedBankAccountRepository> linkedBankAccounts = new();
    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IRedeemRequestRepository> redeemRequests = new();
    private readonly Mock<IStablecoinGateway> gateway = new();
    private readonly Mock<IIdempotencyService> idempotency = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly CreateRedemptionCommandHandler handler;

    public CreateRedemptionCommandHandlerTests()
    {
        idempotency
            .Setup(x => x.TryBeginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyOutcome.Started());
        callerContext.Setup(x => x.CallerId).Returns("client-1");

        handler = new CreateRedemptionCommandHandler(
            linkedBankAccounts.Object,
            subAccounts.Object,
            redeemRequests.Object,
            gateway.Object,
            idempotency.Object,
            unitOfWork.Object,
            new CreateRedemptionCommandValidator(),
            TimeProvider.System,
            callerContext.Object);
    }

    private static LinkedBankAccount ActiveLinkedBankAccount()
    {
        var linkedBankAccount = LinkedBankAccount.Create(
            Guid.NewGuid(), "client-1", "Jane Doe", "1234567890", "021000021", "Test Bank",
            "Jane Doe", "New York", "US", "123 Main St", "10001", null, null, "US", null,
            DateTime.UtcNow);
        linkedBankAccount.SetProviderReference("circle-bank-1", DateTime.UtcNow);
        linkedBankAccount.UpdateStatus(LinkedBankAccountStatus.Active, DateTime.UtcNow);
        return linkedBankAccount;
    }

    private static SubAccount ProvisionedSubAccount()
    {
        var subAccount = SubAccount.Create("client-1", DateTime.UtcNow);
        subAccount.BeginCompliance("circle-wallet-1");
        return subAccount;
    }

    private static CreateRedemptionCommand ValidCommand(Guid? linkedBankAccountId = null) => new(
        LinkedBankAccountId: linkedBankAccountId ?? Guid.NewGuid(),
        GrossAmount: new Money(100m, "USDC"),
        IdempotencyKey: "idem-1",
        CorrelationId: "corr-1");

    [Fact]
    public async Task HandleAsync_WithActiveLinkedBankAccount_ReservesWithExplicitSourceAndPersistsRedemption()
    {
        var linkedBankAccount = ActiveLinkedBankAccount();
        var subAccount = ProvisionedSubAccount();
        var command = ValidCommand(linkedBankAccount.Id);
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(linkedBankAccount.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);
        subAccounts
            .Setup(x => x.GetByClientCompanyIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);
        gateway
            .Setup(x => x.RedeemAsync(It.IsAny<RedeemGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedRedeem("circle-redeem-1", "pending"));

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.LinkedBankAccountId.Should().Be(linkedBankAccount.Id);
        result.GrossAmount.Amount.Should().Be(100m);
        result.Status.Should().Be(TransferStatus.Pending);
        result.CircleRedeemId.Should().Be("circle-redeem-1");

        gateway.Verify(
            x => x.RedeemAsync(
                It.Is<RedeemGatewayRequest>(r =>
                    r.SourceWalletId == "circle-wallet-1" && r.DestinationBankAccountId == "circle-bank-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        redeemRequests.Verify(
            x => x.AddAsync(It.IsAny<RedeemRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // No ledger posting at creation time — only the reserve/complete SaveChangesAsync
        // inside IdempotencyExecutor.
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WithLinkedBankAccountNotActive_ThrowsConflictWithoutCallingGateway()
    {
        var linkedBankAccount = LinkedBankAccount.Create(
            Guid.NewGuid(), "client-1", "Jane Doe", "1234567890", "021000021", "Test Bank",
            "Jane Doe", "New York", "US", "123 Main St", "10001", null, null, "US", null,
            DateTime.UtcNow);
        var command = ValidCommand(linkedBankAccount.Id);
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(linkedBankAccount.Id, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedBankAccount);

        var act = () => handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConflictException>();
        gateway.Verify(
            x => x.RedeemAsync(It.IsAny<RedeemGatewayRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        redeemRequests.Verify(
            x => x.AddAsync(It.IsAny<RedeemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownLinkedBankAccount_ThrowsNotFound()
    {
        var command = ValidCommand();
        linkedBankAccounts
            .Setup(x => x.GetByIdAsync(command.LinkedBankAccountId, "client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LinkedBankAccount?)null);

        var act = () => handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
