namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class SubAccountAlreadyExistsException(string clientCompanyId)
    : DomainException($"A sub-account already exists for client company '{clientCompanyId}'.")
{
    public string ClientCompanyId { get; } = clientCompanyId;
}
