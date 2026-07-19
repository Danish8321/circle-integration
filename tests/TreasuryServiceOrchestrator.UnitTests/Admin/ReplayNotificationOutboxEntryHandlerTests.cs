using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Admin;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Admin;

public sealed class ReplayNotificationOutboxEntryHandlerTests
{
    private readonly Mock<INotificationOutboxRepository> outbox = new();
    private readonly Mock<IAuditLogService> auditLog = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ReplayNotificationOutboxEntryHandler handler;

    public ReplayNotificationOutboxEntryHandlerTests()
    {
        handler = new ReplayNotificationOutboxEntryHandler(
            outbox.Object, auditLog.Object, unitOfWork.Object, callerContext.Object);
    }

    private static NotificationOutboxEntry ExistingEntry() => new()
    {
        Id = Guid.NewGuid(),
        EventType = "SubAccount.Disabled",
        ClientCompanyId = "client-1",
        EntityId = "sub-1",
        OccurredAtUtc = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc),
        CorrelationId = "corr-orig",
        PayloadJson = "{}",
        Status = NotificationDeliveryStatus.Pending,
        AttemptCount = 5,
        NextAttemptAtUtc = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
    };

    private static ReplayNotificationOutboxEntryCommand Command(Guid id) => new(id, "corr-1");

    [Fact]
    public async Task HandleAsync_AsNonAdminCaller_ThrowsTenantForbiddenAndDoesNotSave()
    {
        callerContext.SetupGet(x => x.IsAdmin).Returns(false);

        var act = () => handler.HandleAsync(Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        outbox.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenEntryDoesNotExist_ThrowsNotFoundAndDoesNotSave()
    {
        callerContext.SetupGet(x => x.IsAdmin).Returns(true);
        outbox
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationOutboxEntry?)null);

        var act = () => handler.HandleAsync(Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AsAdmin_ResetsStateAuditsOnceAndSavesOnce()
    {
        callerContext.SetupGet(x => x.CallerId).Returns("apiso-admin");
        callerContext.SetupGet(x => x.IsAdmin).Returns(true);
        var entry = ExistingEntry();
        outbox
            .Setup(x => x.GetByIdAsync(entry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var result = await handler.HandleAsync(Command(entry.Id), TestContext.Current.CancellationToken);

        entry.Status.Should().Be(NotificationDeliveryStatus.Pending);
        entry.AttemptCount.Should().Be(0);
        entry.NextAttemptAtUtc.Should().BeNull();
        result.NotificationOutboxEntryId.Should().Be(entry.Id);
        result.Status.Should().Be(NotificationDeliveryStatus.Pending);
        auditLog.Verify(
            x => x.AppendAsync(
                "NotificationReplayed", "NotificationOutboxEntry", entry.Id.ToString(), It.IsAny<string>(),
                "apiso-admin", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
        auditLog.VerifyNoOtherCalls();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
