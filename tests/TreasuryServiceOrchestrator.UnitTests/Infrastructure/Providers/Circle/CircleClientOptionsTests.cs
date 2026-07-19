using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Providers.Circle;

public sealed class CircleClientOptionsTests
{
    [Fact]
    public void Defaults_MatchDesignDocSpec()
    {
        var options = new CircleClientOptions();

        options.TimeoutSeconds.Should().Be(10);
        options.RetryCount.Should().Be(3);
        options.CircuitBreakerFailureThreshold.Should().Be(5);
        options.CircuitBreakerDurationOfBreak.Should().Be(TimeSpan.FromSeconds(30));
        options.BaseUrl.Should().BeEmpty();
    }

    [Fact]
    public void BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{CircleClientOptions.SectionName}:BaseUrl"] = "https://api.circle.example",
                [$"{CircleClientOptions.SectionName}:TimeoutSeconds"] = "20",
                [$"{CircleClientOptions.SectionName}:RetryCount"] = "5",
                [$"{CircleClientOptions.SectionName}:CircuitBreakerFailureThreshold"] = "8",
                [$"{CircleClientOptions.SectionName}:CircuitBreakerDurationOfBreak"] = "00:01:00",
            })
            .Build();

        var options = new CircleClientOptions();
        configuration.GetSection(CircleClientOptions.SectionName).Bind(options);

        options.BaseUrl.Should().Be("https://api.circle.example");
        options.TimeoutSeconds.Should().Be(20);
        options.RetryCount.Should().Be(5);
        options.CircuitBreakerFailureThreshold.Should().Be(8);
        options.CircuitBreakerDurationOfBreak.Should().Be(TimeSpan.FromMinutes(1));
    }
}
