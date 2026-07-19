using System.Text.Json;
using System.Text.Json.Serialization;

namespace TreasuryServiceOrchestrator.Api.Dtos;

/// <summary>
/// Shape of the envelope <see cref="TreasuryServiceOrchestrator.Infrastructure.Notifications.HttpNotificationSender"/>
/// posts to <c>/internal/notifications</c> (docs/features/13-internal-notifications-outbox.md §4.1).
/// </summary>
public sealed record InternalNotificationEnvelopeRequest(
    [property: JsonRequired] Guid EventId,
    string EventType,
    string ClientCompanyId,
    string EntityId,
    [property: JsonRequired] DateTime OccurredAtUtc,
    string CorrelationId,
    [property: JsonRequired] JsonElement Payload);
