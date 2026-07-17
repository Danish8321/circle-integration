namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

/// <summary>Options bound from the "MockMode" config section, controlling mock provider behavior.</summary>
public sealed class MockProviderOptions
{
    public const string SectionName = "MockMode";

    public bool Enabled { get; set; }

    /// <summary>Probability (0.0-1.0) a mock gateway call throws <c>ProviderUnavailableException</c>.</summary>
    public double FailureInjectionRate { get; set; }

    /// <summary>Delay before scheduled mock webhooks fire.</summary>
    public int WebhookDelayMilliseconds { get; set; } = 200;

    public decimal RedemptionFlatFeeAmount { get; set; } = 1.50m;

    public decimal MainWalletBalanceAmount { get; set; } = 10_000m;
}
