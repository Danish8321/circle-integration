using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.UnitTests.Webhooks;

public sealed class WebhookProcessorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IWebhookInboxRepository> inbox = new();
    private readonly Mock<IWebhookTopicProcessor> topicProcessor = new();
    private readonly Mock<TimeProvider> timeProvider = new();

    public WebhookProcessorTests()
    {
        timeProvider.Setup(x => x.GetUtcNow()).Returns(NowUtc);
        topicProcessor.Setup(x => x.Topic).Returns("externalEntities");
    }

    private WebhookProcessor CreateProcessor(params IWebhookTopicProcessor[] processors) =>
        new(inbox.Object, processors, timeProvider.Object, NullLogger<WebhookProcessor>.Instance);

    private static IncomingWebhookEvent Event(string topic = "externalEntities") =>
        new(topic, "msg-1", "{\"notificationType\":\"externalEntities.updated\"}");

    [Fact]
    public async Task HandleAsync_WhenAlreadySeen_ReturnsProcessedWithoutInvokingProcessor()
    {
        inbox
            .Setup(x => x.TryAddAsync(It.IsAny<WebhookInboxEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var processor = CreateProcessor(topicProcessor.Object);

        var status = await processor.HandleAsync(Event(), TestContext.Current.CancellationToken);

        status.Should().Be(WebhookProcessingStatus.Processed);
        topicProcessor.Verify(
            x => x.ProcessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_MarksProcessedAndReturnsProcessed()
    {
        inbox
            .Setup(x => x.TryAddAsync(It.IsAny<WebhookInboxEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        topicProcessor
            .Setup(x => x.ProcessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var processor = CreateProcessor(topicProcessor.Object);

        var status = await processor.HandleAsync(Event(), TestContext.Current.CancellationToken);

        status.Should().Be(WebhookProcessingStatus.Processed);
        inbox.Verify(x => x.MarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithNoProcessorRegistered_ReturnsUnhandledAndAcknowledges()
    {
        inbox
            .Setup(x => x.TryAddAsync(It.IsAny<WebhookInboxEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var processor = CreateProcessor();

        var status = await processor.HandleAsync(Event("paymentIntents"), TestContext.Current.CancellationToken);

        status.Should().Be(WebhookProcessingStatus.Unhandled);
        inbox.Verify(
            x => x.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenProcessorThrows_ReturnsFailedWithErrorRecorded()
    {
        inbox
            .Setup(x => x.TryAddAsync(It.IsAny<WebhookInboxEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        topicProcessor
            .Setup(x => x.ProcessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var processor = CreateProcessor(topicProcessor.Object);

        var status = await processor.HandleAsync(Event(), TestContext.Current.CancellationToken);

        status.Should().Be(WebhookProcessingStatus.Failed);
        inbox.Verify(x => x.MarkFailedAsync(It.IsAny<Guid>(), "boom", It.IsAny<CancellationToken>()), Times.Once);
    }
}
