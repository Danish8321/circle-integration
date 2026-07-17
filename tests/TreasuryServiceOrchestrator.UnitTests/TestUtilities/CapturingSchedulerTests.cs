using FluentAssertions;

using TreasuryServiceOrchestrator.TestUtilities;

namespace TreasuryServiceOrchestrator.UnitTests.TestUtilities;

public class CapturingSchedulerTests
{
    [Fact]
    public void Scheduled_IsEmpty_BeforeAnyCalls()
    {
        var scheduler = new CapturingScheduler();

        scheduler.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public void Schedule_CapturesCallDetails()
    {
        var scheduler = new CapturingScheduler();

        scheduler.Schedule("deposits.created", "{\"id\":1}", TimeSpan.FromSeconds(5));

        scheduler.Scheduled.Should().ContainSingle()
            .Which.Should().Be(new CapturingScheduler.ScheduledCall(
                "deposits.created", "{\"id\":1}", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Schedule_CapturesMultipleCallsInOrder()
    {
        var scheduler = new CapturingScheduler();

        scheduler.Schedule("topic-1", "payload-1", TimeSpan.FromSeconds(1));
        scheduler.Schedule("topic-2", "payload-2", TimeSpan.FromSeconds(2));

        scheduler.Scheduled.Should().HaveCount(2);
        scheduler.Scheduled[0].Topic.Should().Be("topic-1");
        scheduler.Scheduled[1].Topic.Should().Be("topic-2");
    }
}
