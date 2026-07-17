namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class TenantForbiddenException()
    : DomainException("Caller may not act on the requested tenant.");
