using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class SetSubAccountDisabledHandler(
    ISubAccountRepository subAccounts,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ICallerContext callerContext)
    : ICommandHandler<SetSubAccountDisabledCommand, SetSubAccountDisabledResult>
{
    public async Task<SetSubAccountDisabledResult> HandleAsync(
        SetSubAccountDisabledCommand command, CancellationToken cancellationToken = default)
    {
        // Disable/enable is Admin-only. Defense-in-depth: the controller's scope
        // resolver alone would let a SubAccount caller target its own tenant.
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account for client company '{command.ClientCompanyId}'.");

        if (command.Disabled)
        {
            subAccount.Disable();
        }
        else
        {
            subAccount.Enable();
        }

        await auditLog.AppendAsync(
            "SubAccountDisabledSet", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(new { command.Disabled }),
            callerContext.CallerId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetSubAccountDisabledResult(
            subAccount.Id, subAccount.ClientCompanyId, subAccount.IsDisabled);
    }
}
