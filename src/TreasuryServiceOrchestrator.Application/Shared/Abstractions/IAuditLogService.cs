namespace TreasuryServiceOrchestrator.Application.Shared.Abstractions;

public interface IAuditLogService
{
    Task AppendAsync(
        string eventType,
        string entityType,
        string entityId,
        string payloadJson,
        string clientCompanyId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
