namespace TreasuryServiceOrchestrator.Api.Dtos;

public sealed record ResubmitEntityRegistrationResponse(
    Guid SubAccountId,
    string ClientCompanyId,
    Guid RegistrationId,
    string LifecycleState,
    string RegistrationStatus);
