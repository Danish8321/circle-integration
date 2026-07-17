using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of <see cref="IStablecoinGateway"/> (docs/features/02-mock-mode.md §3.4).
/// Deliberately empty as of ticket 02: <see cref="IStablecoinGateway"/> currently has no members
/// (ticket 01 shipped no Ledger money-moving use cases). Holds in-memory state once a
/// money-moving method is added, so it must stay registered as a singleton, matching
/// <see cref="MockSubAccountGateway"/>. Tickets 03/05/06/07 each extend this class alongside
/// their corresponding <see cref="IStablecoinGateway"/> additions.
/// </summary>
public sealed class MockStablecoinGateway : IStablecoinGateway;
