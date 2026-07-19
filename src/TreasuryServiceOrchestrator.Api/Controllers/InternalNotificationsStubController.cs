using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

/// <summary>
/// Stub receiver for the internal notification outbox dispatcher
/// (docs/features/13-internal-notifications-outbox.md §4.1, Phase-1 ticket 09.6). There is no
/// real downstream internal-notifications service in this repo yet — this endpoint exists so
/// <see cref="TreasuryServiceOrchestrator.Infrastructure.Notifications.NotificationDispatcher"/>
/// has somewhere to actually deliver to for demo/e2e purposes. It does no business processing;
/// it just accepts the envelope and returns 200 OK.
/// </summary>
[ApiController]
[Route("internal/notifications")]
public sealed class InternalNotificationsStubController : ControllerBase
{
    [HttpPost]
    public IActionResult Receive([FromBody] InternalNotificationEnvelopeRequest request) => Ok();
}
