using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Handlers;

public sealed class ProcessExternalEntityDecisionHandlerTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISubAccountRepository> subAccounts = new();
    private readonly Mock<IEntityRegistrationRepository> entityRegistrations = new();
    private readonly Mock<IAuditLogService> auditLog = new();
    private readonly Mock<INotificationOutboxRepository> outbox = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<TimeProvider> timeProvider = new();
    private readonly ProcessExternalEntityDecisionHandler handler;

    public ProcessExternalEntityDecisionHandlerTests()
    {
        timeProvider.Setup(x => x.GetUtcNow()).Returns(NowUtc);

        handler = new ProcessExternalEntityDecisionHandler(
            subAccounts.Object,
            entityRegistrations.Object,
            auditLog.Object,
            outbox.Object,
            unitOfWork.Object,
            timeProvider.Object);
    }

    private static SubAccount PendingSubAccount()
    {
        var subAccount = SubAccount.Create("client-1", DateTime.UtcNow);
        subAccount.BeginCompliance("wallet-123");
        return subAccount;
    }

    private static EntityRegistration PendingRegistration(Guid subAccountId) =>
        EntityRegistration.Create(
            subAccountId, "client-1", "Acme Inc", "EIN-123", "US", "US", "NY", "New York",
            "10001", "Broadway", "1", "wallet-123", DateTime.UtcNow);

    private static ProcessExternalEntityDecisionCommand Command(string complianceState) => new(
        CircleWalletId: "wallet-123",
        ComplianceState: complianceState,
        CorrelationId: "corr-1");

    [Fact]
    public async Task HandleAsync_WithAcceptedState_MarksSubAccountActiveAndRegistrationAccepted()
    {
        var subAccount = PendingSubAccount();
        var registration = PendingRegistration(subAccount.Id);
        subAccounts
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(subAccount.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(registration);

        var result = await handler.HandleAsync(Command("ACCEPTED"), TestContext.Current.CancellationToken);

        result.LifecycleState.Should().Be(SubAccountLifecycleState.Active);
        registration.Status.Should().Be(EntityRegistrationStatus.Accepted);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(
            x => x.AddAsync(
                It.Is<NotificationOutboxEntry>(e =>
                    e.EventType == "EntityRegistrationDecided" && e.EntityId == subAccount.Id.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithRejectedState_MarksSubAccountRejectedAndRegistrationRejected()
    {
        var subAccount = PendingSubAccount();
        var registration = PendingRegistration(subAccount.Id);
        subAccounts
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);
        entityRegistrations
            .Setup(x => x.GetLatestForSubAccountAsync(subAccount.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(registration);

        var result = await handler.HandleAsync(Command("REJECTED"), TestContext.Current.CancellationToken);

        result.LifecycleState.Should().Be(SubAccountLifecycleState.Rejected);
        registration.Status.Should().Be(EntityRegistrationStatus.Rejected);
    }

    [Fact]
    public async Task HandleAsync_WhenSubAccountAlreadyActive_IsNoOpAndDoesNotSave()
    {
        var subAccount = PendingSubAccount();
        subAccount.MarkAccepted();
        subAccounts
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subAccount);

        var result = await handler.HandleAsync(Command("ACCEPTED"), TestContext.Current.CancellationToken);

        result.LifecycleState.Should().Be(SubAccountLifecycleState.Active);
        entityRegistrations.Verify(
            x => x.GetLatestForSubAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownWalletId_ThrowsNotFoundException()
    {
        subAccounts
            .Setup(x => x.GetByCircleWalletIdAsync("wallet-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubAccount?)null);

        var act = () => handler.HandleAsync(Command("ACCEPTED"));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
