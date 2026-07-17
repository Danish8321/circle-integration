namespace TreasuryServiceOrchestrator.Application.Exceptions;

/// <summary>Retryable provider failure — the provider is temporarily unavailable.</summary>
public sealed class ProviderUnavailableException(string message) : DomainException(message);
