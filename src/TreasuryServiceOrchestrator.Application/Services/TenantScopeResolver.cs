using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Services;

public static class TenantScopeResolver
{
    public static TenantScope Resolve(ICallerContext caller, string? requestedClientCompanyId)
    {
        if (caller.IsAdmin)
        {
            return requestedClientCompanyId is null
                ? new TenantScope.AllTenants()
                : new TenantScope.SingleTenant(requestedClientCompanyId);
        }

        if (requestedClientCompanyId is not null
            && !string.Equals(requestedClientCompanyId, caller.CallerId, StringComparison.Ordinal))
        {
            throw new TenantForbiddenException();
        }

        return new TenantScope.SingleTenant(caller.CallerId);
    }
}
