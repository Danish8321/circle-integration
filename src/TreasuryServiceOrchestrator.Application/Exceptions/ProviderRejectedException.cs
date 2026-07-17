namespace TreasuryServiceOrchestrator.Application.Exceptions;

/// <summary>Terminal provider rejection — retrying will not succeed.</summary>
public sealed class ProviderRejectedException(string message) : DomainException(message);
