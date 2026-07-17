using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.ProcessExternalEntityDecision;

public sealed class ProcessExternalEntityDecisionHandler(
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<ProcessExternalEntityDecisionCommand, ProcessExternalEntityDecisionResult>
{
    public async Task<ProcessExternalEntityDecisionResult> HandleAsync(
        ProcessExternalEntityDecisionCommand command, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByCircleWalletIdAsync(command.CircleWalletId, cancellationToken)
            ?? throw new NotFoundException(
                $"No sub-account found for Circle wallet '{command.CircleWalletId}'.");

        var registrationStatus = EntityRegistrationStatusMapper.Map(command.ComplianceState);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Redelivered/replayed decision events are a no-op once the sub-account already left
        // PendingCompliance — the pipeline's own dedup (webhook inbox) is the first line of
        // defense, but this handler must still be safe to invoke twice (§2.6 replay contract).
        if (subAccount.LifecycleState != SubAccountLifecycleState.PendingCompliance)
        {
            return new ProcessExternalEntityDecisionResult(
                subAccount.Id, subAccount.ClientCompanyId, subAccount.LifecycleState);
        }

        var registration = await entityRegistrations.GetLatestForSubAccountAsync(subAccount.Id, cancellationToken)
            ?? throw new NotFoundException(
                $"No entity registration found for sub-account '{subAccount.Id}'.");

        switch (registrationStatus)
        {
            case EntityRegistrationStatus.Accepted:
                registration.Accept(nowUtc);
                subAccount.MarkAccepted();
                break;
            case EntityRegistrationStatus.Rejected:
                registration.Reject("Rejected by Circle compliance decision.", nowUtc);
                subAccount.MarkRejected();
                break;
            case EntityRegistrationStatus.Pending:
            default:
                // PENDING never arrives on this webhook topic in practice (it is the synchronous
                // create-time response, not a decision) — treated as a no-op rather than a throw
                // so an unexpected redelivery doesn't dead-letter the event.
                return new ProcessExternalEntityDecisionResult(
                    subAccount.Id, subAccount.ClientCompanyId, subAccount.LifecycleState);
        }

        await auditLog.AppendAsync(
            "ExternalEntityDecisionProcessed", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(new { command.CircleWalletId, registrationStatus }),
            subAccount.ClientCompanyId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessExternalEntityDecisionResult(
            subAccount.Id, subAccount.ClientCompanyId, subAccount.LifecycleState);
    }
}
