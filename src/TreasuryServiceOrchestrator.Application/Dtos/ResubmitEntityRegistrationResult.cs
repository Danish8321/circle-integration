namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record ResubmitEntityRegistrationResult(
    Guid SubAccountId,
    string ClientCompanyId,
    Guid RegistrationId,
    string LifecycleState,
    string RegistrationStatus);
