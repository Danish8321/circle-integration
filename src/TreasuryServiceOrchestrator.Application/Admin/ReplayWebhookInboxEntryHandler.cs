using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed class ReplayWebhookInboxEntryHandler(
    WebhookProcessor webhookProcessor,
    IWebhookInboxRepository inbox,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ICallerContext callerContext)
{
    public async Task<ReplayWebhookInboxEntryResult> HandleAsync(
        ReplayWebhookInboxEntryCommand command, CancellationToken cancellationToken = default)
    {
        // Admin-only. Defense-in-depth: mirrors SetSubAccountDisabledHandler (08.3 precedent) —
        // the controller gates too, but the handler must not trust that alone.
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var entry = await inbox.GetByIdAsync(command.WebhookInboxEntryId, cancellationToken)
            ?? throw new NotFoundException($"No webhook inbox entry '{command.WebhookInboxEntryId}'.");

        var status = await webhookProcessor.ReplayAsync(command.WebhookInboxEntryId, cancellationToken);

        await auditLog.AppendAsync(
            "WebhookReplayed", "WebhookInboxEntry", entry.Id.ToString(),
            JsonSerializer.Serialize(new { entry.Topic, Status = status.ToString() }),
            callerContext.CallerId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReplayWebhookInboxEntryResult(entry.Id, entry.Topic, status);
    }
}
