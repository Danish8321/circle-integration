namespace TreasuryServiceOrchestrator.Domain;

public class NotificationOutboxEntry
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public required string ClientCompanyId { get; set; }
    public required string EntityId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public required string CorrelationId { get; set; }
    public required string PayloadJson { get; set; }
    public NotificationDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}
