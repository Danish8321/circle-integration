namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>
/// Source of pseudo-random values for mock provider gateways (failure injection, mock id
/// generation). Abstracted so tests can supply deterministic implementations.
/// </summary>
public interface IMockRandomSource
{
    /// <summary>Returns a random double in [0.0, 1.0) for probability checks against
    /// <see cref="MockProviderOptions.FailureInjectionRate"/>.</summary>
    double NextDouble();

    /// <summary>Returns a new <see cref="Guid"/> for mock provider ids (e.g. <c>deposit-{guid}</c>).</summary>
    Guid NewGuid();
}
