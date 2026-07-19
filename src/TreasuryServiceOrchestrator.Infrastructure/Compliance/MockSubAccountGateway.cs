using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.Options;

using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Compliance;

/// <summary>
/// Mock implementation of <see cref="ISubAccountGateway"/> (docs/features/02-mock-mode.md §3.3).
/// Holds in-memory entity-registration state, so it must be registered as a singleton — state
/// has to survive across requests within the process. Terminal compliance state is decided
/// deterministically from the submitted business name and delivered asynchronously through a
/// scheduled "externalEntities" webhook fed into the real webhook pipeline (ADR 0007), matching
/// Circle's real async-provider semantics: the synchronous call only returns "Pending".
/// </summary>
public sealed class MockSubAccountGateway(
    IOptions<MockProviderOptions> options,
    IMockWebhookScheduler webhookScheduler,
    IMockRandomSource randomSource) : ISubAccountGateway
{
    private readonly ConcurrentDictionary<string, EntityRegistrationRecord> registrations = new(StringComparer.Ordinal);

    public Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken = default)
    {
        MaybeThrowProviderUnavailable();

        var walletId = $"mock-wallet-{randomSource.NewGuid()}";
        var terminalState = request.BusinessName.EndsWith(
            options.Value.RejectBusinessNameSuffix, StringComparison.OrdinalIgnoreCase)
            ? "Rejected"
            : "Accepted";

        registrations[walletId] = new EntityRegistrationRecord(
            terminalState, request.BusinessName, request.BusinessUniqueIdentifier);

        var envelope = new ExternalEntityWebhookEnvelope
        {
            NotificationType = "externalEntities.update",
            ExternalEntity = new ExternalEntityCircleData
            {
                WalletId = walletId,
                BusinessName = request.BusinessName,
                BusinessUniqueIdentifier = request.BusinessUniqueIdentifier,
                ComplianceState = terminalState,
            },
        };

        webhookScheduler.Schedule(
            "externalEntities",
            JsonSerializer.Serialize(envelope),
            TimeSpan.FromMilliseconds(options.Value.WebhookDelayMilliseconds));

        return Task.FromResult(new CreateExternalEntityResult(
            walletId, "Pending", request.BusinessName, request.BusinessUniqueIdentifier));
    }

    public Task<CreateExternalEntityResult> GetExternalEntityAsync(
        string walletId, CancellationToken cancellationToken = default)
    {
        MaybeThrowProviderUnavailable();

        if (registrations.TryGetValue(walletId, out var record))
        {
            return Task.FromResult(new CreateExternalEntityResult(
                walletId, record.ComplianceState, record.BusinessName, record.BusinessUniqueIdentifier));
        }

        return Task.FromResult(new CreateExternalEntityResult(walletId, "Pending", string.Empty, string.Empty));
    }

    private void MaybeThrowProviderUnavailable()
    {
        if (randomSource.NextDouble() < options.Value.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated failure injection.");
        }
    }

    private sealed record EntityRegistrationRecord(
        string ComplianceState, string BusinessName, string BusinessUniqueIdentifier);
}
