using FluentAssertions;
using Moq;
using TreasuryServiceOrchestrator.Application.Admin;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Admin;

public sealed class ReplayWebhookInboxEntryHandlerTests
{
    private readonly Mock<IWebhookInboxRepository> inbox = new();
    private readonly Mock<IWebhookTopicProcessor> topicProcessor = new();
    private readonly Mock<TimeProvider> timeProvider = new();
    private readonly Mock<IAuditLogService> auditLog = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<ICallerContext> callerContext = new();
    private readonly ReplayWebhookInboxEntryHandler handler;

    public ReplayWebhookInboxEntryHandlerTests()
    {
        timeProvider.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
        topicProcessor.Setup(x => x.Topic).Returns("externalEntities");
        var webhookProcessor = new WebhookProcessor(inbox.Object, [topicProcessor.Object], timeProvider.Object);
        handler = new ReplayWebhookInboxEntryHandler(
            webhookProcessor, inbox.Object, auditLog.Object, unitOfWork.Object, callerContext.Object);
    }

    private static WebhookInboxEntry ExistingEntry() => new()
    {
        Id = Guid.NewGuid(),
        Topic = "externalEntities",
        CircleEventId = "evt-1",
        PayloadJson = "{}",
        ReceivedAtUtc = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc),
        Processed = false,
        Attempts = 5,
        LastError = "boom",
    };

    private static ReplayWebhookInboxEntryCommand Command(Guid id) => new(id, "corr-1");

    [Fact]
    public async Task HandleAsync_AsNonAdminCaller_ThrowsTenantForbiddenAndDoesNotSave()
    {
        callerContext.SetupGet(x => x.IsAdmin).Returns(false);
        var entry = ExistingEntry();

        var act = () => handler.HandleAsync(Command(entry.Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TenantForbiddenException>();
        inbox.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenEntryDoesNotExist_ThrowsNotFoundAndDoesNotSave()
    {
        callerContext.SetupGet(x => x.IsAdmin).Returns(true);
        inbox
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookInboxEntry?)null);

        var act = () => handler.HandleAsync(Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AsAdmin_ResetsAttemptsAudistOnceAndSavesOnce()
    {
        callerContext.SetupGet(x => x.CallerId).Returns("apiso-admin");
        callerContext.SetupGet(x => x.IsAdmin).Returns(true);
        var entry = ExistingEntry();
        inbox
            .Setup(x => x.GetByIdAsync(entry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        topicProcessor
            .Setup(x => x.ProcessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await handler.HandleAsync(Command(entry.Id), TestContext.Current.CancellationToken);

        result.WebhookInboxEntryId.Should().Be(entry.Id);
        result.Status.Should().Be(WebhookProcessingStatus.Processed);
        inbox.Verify(x => x.ResetForReplayAsync(entry.Id, It.IsAny<CancellationToken>()), Times.Once);
        inbox.Verify(x => x.MarkProcessedAsync(entry.Id, It.IsAny<CancellationToken>()), Times.Once);
        auditLog.Verify(
            x => x.AppendAsync(
                "WebhookReplayed", "WebhookInboxEntry", entry.Id.ToString(), It.IsAny<string>(),
                "apiso-admin", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
        auditLog.VerifyNoOtherCalls();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
