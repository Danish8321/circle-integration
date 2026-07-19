using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class AuditLogService(TreasuryServiceOrchestratorDbContext dbContext, TimeProvider timeProvider)
    : IAuditLogService
{
    public async Task AppendAsync(
        string eventType,
        string entityType,
        string entityId,
        string payloadJson,
        string clientCompanyId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var record = AuditRecord.Create(
            eventType, entityType, entityId, payloadJson, clientCompanyId, correlationId,
            timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.AuditRecords.AddAsync(record, cancellationToken);
    }
}
