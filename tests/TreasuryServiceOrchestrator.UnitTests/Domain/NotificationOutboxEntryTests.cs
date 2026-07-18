using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public sealed class NotificationOutboxEntryTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_Entry_DefaultsToPendingStatus()
    {
        var entry = new NotificationOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = "DepositCredited",
            ClientCompanyId = "client-1",
            EntityId = Guid.NewGuid().ToString(),
            OccurredAtUtc = NowUtc,
            CorrelationId = "correlation-1",
            PayloadJson = "{}",
        };

        Assert.Equal(NotificationDeliveryStatus.Pending, entry.Status);
        Assert.Equal(0, entry.AttemptCount);
        Assert.Null(entry.NextAttemptAtUtc);
        Assert.Null(entry.DeliveredAtUtc);
    }

    [Fact]
    public void Setting_StatusDelivered_SetsDeliveredAtUtc()
    {
        var entry = new NotificationOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = "DepositCredited",
            ClientCompanyId = "client-1",
            EntityId = Guid.NewGuid().ToString(),
            OccurredAtUtc = NowUtc,
            CorrelationId = "correlation-1",
            PayloadJson = "{}",
        };

        entry.Status = NotificationDeliveryStatus.Delivered;
        entry.DeliveredAtUtc = NowUtc;

        Assert.Equal(NotificationDeliveryStatus.Delivered, entry.Status);
        Assert.Equal(NowUtc, entry.DeliveredAtUtc);
    }
}
