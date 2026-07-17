namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class ConflictException(string message) : DomainException(message);
