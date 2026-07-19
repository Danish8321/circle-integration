using FluentAssertions;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public sealed class MockWebhookChannelTests
{
    [Fact]
    public void DequeueDue_ItemScheduledInFuture_IsNotReturnedBeforeDue()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero));
        var channel = new MockWebhookChannel(timeProvider);

        channel.Schedule("deposits", "{}", TimeSpan.FromSeconds(30));

        var due = channel.DequeueDue(timeProvider.GetUtcNow().UtcDateTime);

        due.Should().BeEmpty();
    }

    [Fact]
    public void DequeueDue_ItemPastDeliveryTime_IsReturnedOnceAndRemoved()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero));
        var channel = new MockWebhookChannel(timeProvider);

        channel.Schedule("deposits", "{\"a\":1}", TimeSpan.FromSeconds(5));
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        var firstPoll = channel.DequeueDue(timeProvider.GetUtcNow().UtcDateTime);
        var secondPoll = channel.DequeueDue(timeProvider.GetUtcNow().UtcDateTime);

        firstPoll.Should().ContainSingle(w => w.Topic == "deposits" && w.PayloadJson == "{\"a\":1}");
        secondPoll.Should().BeEmpty();
    }

    [Fact]
    public void DequeueDue_MultipleDueItems_AreAllReturnedInOnePoll()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero));
        var channel = new MockWebhookChannel(timeProvider);

        channel.Schedule("deposits", "{\"a\":1}", TimeSpan.FromSeconds(1));
        channel.Schedule("transfers", "{\"b\":2}", TimeSpan.FromSeconds(2));
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        var due = channel.DequeueDue(timeProvider.GetUtcNow().UtcDateTime);

        due.Should().HaveCount(2);
    }
}
