using FluentAssertions;
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.UnitTests.Webhooks;

public sealed class WebhookDeadLetterPolicyTests
{
    private static WebhookInboxEntry Entry(int attempts, bool processed) => new()
    {
        Id = Guid.NewGuid(),
        Topic = "externalEntities",
        CircleEventId = "msg-1",
        PayloadJson = "{}",
        Attempts = attempts,
        Processed = processed,
    };

    [Fact]
    public void IsDeadLettered_BelowThreshold_ReturnsFalse()
    {
        Entry(attempts: WebhookDeadLetterPolicy.AttemptThreshold - 1, processed: false)
            .IsDeadLettered().Should().BeFalse();
    }

    [Fact]
    public void IsDeadLettered_AtThreshold_ReturnsTrue()
    {
        Entry(attempts: WebhookDeadLetterPolicy.AttemptThreshold, processed: false)
            .IsDeadLettered().Should().BeTrue();
    }

    [Fact]
    public void IsDeadLettered_AtThresholdButProcessed_ReturnsFalse()
    {
        Entry(attempts: WebhookDeadLetterPolicy.AttemptThreshold, processed: true)
            .IsDeadLettered().Should().BeFalse();
    }
}
