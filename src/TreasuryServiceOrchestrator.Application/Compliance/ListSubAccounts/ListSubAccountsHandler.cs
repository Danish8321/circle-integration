using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.GetSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Compliance.ListSubAccounts;

public sealed class ListSubAccountsHandler(
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations,
    IFundAccountRepository fundAccounts,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<SubAccountDetailsResult>> HandleAsync(
        ListSubAccountsQuery query, CancellationToken cancellationToken = default)
    {
        // Listing is Admin/all-tenant only: a SubAccount caller resolving to
        // SingleTenant may not list. Defense-in-depth: also require the caller
        // itself to be Admin, matching CreateSubAccountHandler.
        if (!callerContext.IsAdmin || query.Scope is not TenantScope.AllTenants)
        {
            throw new TenantForbiddenException();
        }

        // All-tenant access is itself audited (PRD §2.4).
        await auditLog.AppendAsync(
            "SubAccountsListed", "SubAccount", "*",
            JsonSerializer.Serialize(new { LifecycleState = query.LifecycleState?.ToString() }),
            callerContext.CallerId, query.CorrelationId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var listed = await subAccounts.ListAsync(query.LifecycleState, cancellationToken);

        var results = new List<SubAccountDetailsResult>(listed.Count);
        foreach (var subAccount in listed)
        {
            var registration = await entityRegistrations.GetLatestForSubAccountAsync(
                subAccount.Id, cancellationToken);
            var fundAccount = await fundAccounts.FindByClientCompanyIdAsync(
                subAccount.ClientCompanyId, cancellationToken);
            var mapped = SubAccountDetailsMapper.Map(subAccount, registration);
            results.Add(mapped with { CurrentBalance = fundAccount?.Balance });
        }

        return results;
    }
}
