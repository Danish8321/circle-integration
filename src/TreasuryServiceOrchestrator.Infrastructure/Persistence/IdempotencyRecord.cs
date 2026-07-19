namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; }

    /// <summary>Null while <see cref="Status"/> is <see cref="IdempotencyStatus.InProgress"/>.</summary>
    public string? ResultJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
