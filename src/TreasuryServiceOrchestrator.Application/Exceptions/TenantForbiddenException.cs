namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class TenantForbiddenException()
    : Exception("Caller may not act on the requested tenant.");
