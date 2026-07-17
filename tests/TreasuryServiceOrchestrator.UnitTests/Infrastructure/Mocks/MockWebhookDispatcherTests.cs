using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public sealed class MockWebhookDispatcherTests
{
    private readonly Mock<IWebhookInboxRepository> inbox = new();
    private readonly Mock<IWebhookTopicProcessor> topicProcessor = new();
    private readonly FakeTimeProvider timeProvider =
        new(new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero));

    public MockWebhookDispatcherTests()
    {
        topicProcessor.Setup(x => x.Topic).Returns("deposits");
        inbox
            .Setup(x => x.TryAddAsync(It.IsAny<WebhookInboxEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        topicProcessor
            .Setup(x => x.ProcessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private (MockWebhookChannel Channel, MockWebhookDispatcher Dispatcher) CreateSut()
    {
        var services = new ServiceCollection();
        services.AddSingleton(inbox.Object);
        services.AddSingleton<IEnumerable<IWebhookTopicProcessor>>([topicProcessor.Object]);
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddScoped<WebhookProcessor>();
        var provider = services.BuildServiceProvider();

        var channel = new MockWebhookChannel(timeProvider);
        var dispatcher = new MockWebhookDispatcher(
            channel, provider.GetRequiredService<IServiceScopeFactory>(), timeProvider);

        return (channel, dispatcher);
    }

    [Fact]
    public async Task DispatchDueAsync_WebhookScheduledInFuture_IsNotDispatchedBeforeDue()
    {
        var (channel, dispatcher) = CreateSut();
        channel.Schedule("deposits", "{\"a\":1}", TimeSpan.FromMinutes(5));

        await dispatcher.DispatchDueAsync(TestContext.Current.CancellationToken);

        topicProcessor.Verify(
            x => x.ProcessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchDueAsync_DueWebhook_ReachesRealPipelineExactlyOnce()
    {
        var (channel, dispatcher) = CreateSut();
        channel.Schedule("deposits", "{\"a\":1}", TimeSpan.FromSeconds(1));
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        await dispatcher.DispatchDueAsync(TestContext.Current.CancellationToken);

        topicProcessor.Verify(
            x => x.ProcessAsync("{\"a\":1}", It.IsAny<CancellationToken>()), Times.Once);
        inbox.Verify(
            x => x.MarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchDueAsync_MultipleDueWebhooks_AreAllDispatchedInOnePoll()
    {
        var (channel, dispatcher) = CreateSut();
        channel.Schedule("deposits", "{\"a\":1}", TimeSpan.FromSeconds(1));
        channel.Schedule("deposits", "{\"b\":2}", TimeSpan.FromSeconds(1));
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        await dispatcher.DispatchDueAsync(TestContext.Current.CancellationToken);

        topicProcessor.Verify(
            x => x.ProcessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
