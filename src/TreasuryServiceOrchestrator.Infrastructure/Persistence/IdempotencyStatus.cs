namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public enum IdempotencyStatus
{
    /// <summary>Reserved before the provider call; work not yet completed. Re-drivable.</summary>
    InProgress = 0,

    /// <summary>Work finished; <see cref="IdempotencyRecord.ResultJson"/> holds the cached result.</summary>
    Completed = 1,
}
