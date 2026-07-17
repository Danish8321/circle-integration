namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
