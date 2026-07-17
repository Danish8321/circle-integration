namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class NotFoundException(string message) : DomainException(message);
