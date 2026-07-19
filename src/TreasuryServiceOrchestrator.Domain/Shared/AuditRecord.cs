namespace TreasuryServiceOrchestrator.Domain.Shared;

public class AuditRecord
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public string ClientCompanyId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    private AuditRecord()
    {
    }

    public static AuditRecord Create(
        string eventType,
        string entityType,
        string entityId,
        string payloadJson,
        string clientCompanyId,
        string correlationId,
        DateTime nowUtc)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            PayloadJson = payloadJson,
            ClientCompanyId = clientCompanyId,
            CorrelationId = correlationId,
            OccurredAtUtc = nowUtc,
        };
    }
}
