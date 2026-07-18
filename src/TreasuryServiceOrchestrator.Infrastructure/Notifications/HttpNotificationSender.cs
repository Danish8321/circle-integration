using System.Net.Http.Json;
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
public sealed class HttpNotificationSender(HttpClient httpClient, IOptions<NotificationDispatcherOptions> options)
    : INotificationSender
{
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
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static System.Text.Json.JsonElement JsonDocumentPayload(string payloadJson) =>
        System.Text.Json.JsonDocument.Parse(payloadJson).RootElement;
}
