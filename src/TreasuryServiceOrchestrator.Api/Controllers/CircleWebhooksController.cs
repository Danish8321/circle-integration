using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("v1/webhooks/circle")]
public sealed class CircleWebhooksController(
    ISnsSignatureVerifier signatureVerifier, WebhookProcessor webhookProcessor) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(
        [FromBody] CircleSnsEnvelopeRequest request, CancellationToken cancellationToken)
    {
        var snsEnvelope = new SnsEnvelope(
            request.Type, request.MessageId, request.TopicArn, request.Message,
            request.Signature, request.SigningCertURL);

        if (!await signatureVerifier.VerifyAsync(snsEnvelope, cancellationToken))
        {
            return Forbid();
        }

        // Handshake message, not a business event — completes via a server-side GET to
        // SubscribeURL (docs/features/03 §3.3), not wired in this Phase-1-scoped slice.
        // No inbox row is written for it.
        if (string.Equals(request.Type, "SubscriptionConfirmation", StringComparison.Ordinal)
            || string.Equals(request.Type, "UnsubscribeConfirmation", StringComparison.Ordinal))
        {
            return Ok();
        }

        var innerEnvelope = JsonSerializer.Deserialize<CircleNotificationEnvelope>(request.Message)
            ?? throw new InvalidOperationException("Empty Circle notification envelope.");

        var incoming = new IncomingWebhookEvent(
            innerEnvelope.NotificationType, request.MessageId, request.Message);

        var status = await webhookProcessor.HandleAsync(incoming, cancellationToken);

        return status switch
        {
            WebhookProcessingStatus.Processed => Ok(),
            WebhookProcessingStatus.Unhandled => Ok(),
            WebhookProcessingStatus.Failed => StatusCode(StatusCodes.Status500InternalServerError),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
