using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Notifications;
using TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Notifications;

public sealed class NotificationDispatcherTests
{
    private readonly Mock<INotificationOutboxRepository> outbox = new();
    private readonly Mock<INotificationSender> sender = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly FakeTimeProvider timeProvider =
        new(new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero));
    private readonly NotificationDispatcherOptions options = new();

    private NotificationDispatcher CreateSut()
    {
        var services = new ServiceCollection();
        services.AddSingleton(outbox.Object);
        services.AddSingleton(sender.Object);
        services.AddSingleton(unitOfWork.Object);
        var provider = services.BuildServiceProvider();

        return new NotificationDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            timeProvider,
            NullLogger<NotificationDispatcher>.Instance);
    }

    private static NotificationOutboxEntry CreateEntry() => new()
    {
        Id = Guid.NewGuid(),
        EventType = "DepositCredited",
        ClientCompanyId = "tenant-1",
        EntityId = Guid.NewGuid().ToString(),
        OccurredAtUtc = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc),
        CorrelationId = "corr-1",
        PayloadJson = "{}",
        Status = NotificationDeliveryStatus.Pending,
    };

    [Fact]
    public async Task DispatchDueBatchAsync_SenderSucceeds_MarksDeliveredAndSaves()
    {
        var entry = CreateEntry();
        outbox
            .Setup(x => x.GetDueBatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        sender
            .Setup(x => x.SendAsync(entry, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();
        var count = await sut.DispatchDueBatchAsync(TestContext.Current.CancellationToken);

        count.Should().Be(1);
        entry.Status.Should().Be(NotificationDeliveryStatus.Delivered);
        entry.DeliveredAtUtc.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchDueBatchAsync_SenderFails_IncrementsAttemptAndPushesBackoff()
    {
        var entry = CreateEntry();
        outbox
            .Setup(x => x.GetDueBatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        sender
            .Setup(x => x.SendAsync(entry, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();
        var count = await sut.DispatchDueBatchAsync(TestContext.Current.CancellationToken);

        count.Should().Be(1);
        entry.Status.Should().Be(NotificationDeliveryStatus.Pending);
        entry.AttemptCount.Should().Be(1);
        entry.NextAttemptAtUtc.Should().BeAfter(timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(-1));
        entry.NextAttemptAtUtc.Should().Be(
            timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(options.BaseBackoffMilliseconds * 2));
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchDueBatchAsync_NoDueEntries_ReturnsZeroAndDoesNotSave()
    {
        outbox
            .Setup(x => x.GetDueBatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var count = await sut.DispatchDueBatchAsync(TestContext.Current.CancellationToken);

        count.Should().Be(0);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
