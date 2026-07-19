using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Notifications;

/// <summary>
/// Posts the internal-notification envelope to the configured internal-service endpoint
/// (docs/features/13-internal-notifications-outbox.md §4.1). Registered via
/// <c>AddHttpClient&lt;INotificationSender, HttpNotificationSender&gt;()</c> — never
/// <c>new HttpClient()</c> (invariant 3).
/// </summary>
public sealed partial class HttpNotificationSender(
    HttpClient httpClient, IOptions<NotificationDispatcherOptions> options, ILogger<HttpNotificationSender> logger)
    : INotificationSender
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Notification delivery failed for outbox entry {EntryId} (event {EventType}) to {EndpointUrl}")]
    private partial void LogDeliveryFailed(Exception ex, Guid entryId, string eventType, string endpointUrl);

    public async Task<bool> SendAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.EndpointUrl)
        {
            Content = JsonContent.Create(new
            {
                eventId = entry.Id,
                eventType = entry.EventType,
                clientCompanyId = entry.ClientCompanyId,
                entityId = entry.EntityId,
                occurredAtUtc = entry.OccurredAtUtc,
                correlationId = entry.CorrelationId,
                payload = JsonDocumentPayload(entry.PayloadJson),
            }),
        };

        if (!string.IsNullOrEmpty(settings.AuthHeaderName))
        {
            request.Headers.TryAddWithoutValidation(settings.AuthHeaderName, settings.AuthHeaderValue);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            LogDeliveryFailed(ex, entry.Id, entry.EventType, settings.EndpointUrl);
            return false;
        }
    }

    private static System.Text.Json.JsonElement JsonDocumentPayload(string payloadJson) =>
        System.Text.Json.JsonDocument.Parse(payloadJson).RootElement;
}
