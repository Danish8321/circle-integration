using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed class ReplayNotificationOutboxEntryHandler(
    INotificationOutboxRepository outbox,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ICallerContext callerContext)
{
    public async Task<ReplayNotificationOutboxEntryResult> HandleAsync(
        ReplayNotificationOutboxEntryCommand command, CancellationToken cancellationToken = default)
    {
        // Admin-only. Defense-in-depth: mirrors SetSubAccountDisabledHandler (08.3 precedent) —
        // the controller gates too, but the handler must not trust that alone.
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var entry = await outbox.GetByIdAsync(command.NotificationOutboxEntryId, cancellationToken)
            ?? throw new NotFoundException($"No notification outbox entry '{command.NotificationOutboxEntryId}'.");

        // Reset so NotificationDispatcher's GetDueBatchAsync query (Status == Pending &&
        // NextAttemptAtUtc is due) picks this entry up again on its next poll — no bespoke
        // second delivery path.
        entry.Status = NotificationDeliveryStatus.Pending;
        entry.AttemptCount = 0;
        entry.NextAttemptAtUtc = null;
        entry.DeliveredAtUtc = null;

        await auditLog.AppendAsync(
            "NotificationReplayed", "NotificationOutboxEntry", entry.Id.ToString(),
            JsonSerializer.Serialize(new { entry.EventType }),
            callerContext.CallerId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReplayNotificationOutboxEntryResult(entry.Id, entry.EventType, entry.Status);
    }
}
