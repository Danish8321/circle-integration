namespace TreasuryServiceOrchestrator.Application.Services;

/// <summary>Options bound from the "SupportedChains" config section, controlling which Circle
/// chain codes (e.g. "ETH") are allowed for deposit address generation.</summary>
public sealed class SupportedChainsOptions
{
    public const string SectionName = "SupportedChains";

    public IList<string> Chains { get; set; } = ["ETH"];

    /// <summary>Case-sensitive exact match against Circle chain codes (e.g. "ETH").</summary>
    public bool IsSupported(string chain) => Chains.Contains(chain, StringComparer.Ordinal);
}
