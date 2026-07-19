using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Exceptions;

/// <summary>
/// Thrown when an inbound deposit event cannot be resolved to a valid
/// <see cref="TreasuryServiceOrchestrator.Domain.DepositSourceType"/> (e.g. an unrecognized
/// webhook event type). Not thrown by <see cref="ProcessDepositCommandHandler"/> itself —
/// DepositSourceType is required and validated on the command — this is for the webhook
/// routing layer (ticket 04.6) that resolves a raw provider event into a
/// <see cref="ProcessDepositCommand"/>.
/// </summary>
public sealed class DepositSourceNotResolvedException(string message) : DomainException(message);
