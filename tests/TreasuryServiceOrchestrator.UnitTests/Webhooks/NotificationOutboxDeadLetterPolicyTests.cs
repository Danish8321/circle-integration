using FluentAssertions;
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.UnitTests.Webhooks;

public sealed class NotificationOutboxDeadLetterPolicyTests
{
    private static NotificationOutboxEntry Entry(int attemptCount, NotificationDeliveryStatus status) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "deposit.completed",
        ClientCompanyId = "client-1",
        EntityId = "entity-1",
        OccurredAtUtc = DateTime.UtcNow,
        CorrelationId = "corr-1",
        PayloadJson = "{}",
        Status = status,
        AttemptCount = attemptCount,
    };

    [Fact]
    public void IsDeadLettered_BelowThreshold_ReturnsFalse()
    {
        Entry(attemptCount: NotificationOutboxDeadLetterPolicy.AttemptThreshold - 1, status: NotificationDeliveryStatus.Pending)
            .IsDeadLettered().Should().BeFalse();
    }

    [Fact]
    public void IsDeadLettered_AtThreshold_ReturnsTrue()
    {
        Entry(attemptCount: NotificationOutboxDeadLetterPolicy.AttemptThreshold, status: NotificationDeliveryStatus.Pending)
            .IsDeadLettered().Should().BeTrue();
    }

    [Fact]
    public void IsDeadLettered_AtThresholdButDelivered_ReturnsFalse()
    {
        Entry(attemptCount: NotificationOutboxDeadLetterPolicy.AttemptThreshold, status: NotificationDeliveryStatus.Delivered)
            .IsDeadLettered().Should().BeFalse();
    }
}
