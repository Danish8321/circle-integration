using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Admin;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Api.Admin;

[ApiController]
[Route("v1/admin")]
public sealed class DeadLetterController(
    ReplayWebhookInboxEntryHandler replayWebhookInboxEntryHandler,
    ReplayNotificationOutboxEntryHandler replayNotificationOutboxEntryHandler,
    ICallerContext callerContext) : ControllerBase
{
    [HttpPost("webhooks/{id:guid}/replay")]
    public async Task<ActionResult<ReplayWebhookInboxEntryResponse>> ReplayWebhookInboxEntry(
        Guid id, CancellationToken cancellationToken)
    {
        // No route segment for TenantScopeResolver to arbitrate against, so the Admin gate is
        // enforced directly here rather than via TenantScopeResolver — same pattern as
        // AdminTransactionsController/MasterAccountController (08.3).
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var result = await replayWebhookInboxEntryHandler.HandleAsync(
            new ReplayWebhookInboxEntryCommand(id, HttpContext.TraceIdentifier), cancellationToken);

        return Ok(new ReplayWebhookInboxEntryResponse(
            result.WebhookInboxEntryId, result.Topic, result.Status.ToString()));
    }

    [HttpPost("notifications/{id:guid}/replay")]
    public async Task<ActionResult<ReplayNotificationOutboxEntryResponse>> ReplayNotificationOutboxEntry(
        Guid id, CancellationToken cancellationToken)
    {
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var result = await replayNotificationOutboxEntryHandler.HandleAsync(
            new ReplayNotificationOutboxEntryCommand(id, HttpContext.TraceIdentifier), cancellationToken);

        return Ok(new ReplayNotificationOutboxEntryResponse(
            result.NotificationOutboxEntryId, result.EventType, result.Status.ToString()));
    }
}
