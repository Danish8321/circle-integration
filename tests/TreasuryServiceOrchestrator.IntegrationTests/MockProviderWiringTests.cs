using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using TreasuryServiceOrchestrator.Infrastructure.Mocks;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class MockProviderWiringTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    [Fact]
    public void MockModeEnabledInDevelopment_ResolvesMockSubAccountGateway()
    {
        using var scope = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["MockMode:Enabled"] = "true",
                });
            });
        }).Services.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<ISubAccountGateway>();

        Assert.IsType<MockSubAccountGateway>(gateway);
    }

    [Fact]
    public void MockModeEnabledInProduction_ThrowsOnStartup()
    {
        var previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("MockMode__Enabled", "true");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__TreasuryServiceOrchestrator",
            "Server=(localdb)\\mssqllocaldb;Database=TreasuryServiceOrchestrator;Trusted_Connection=True;MultipleActiveResultSets=true");

        try
        {
            using var productionFactory = new WebApplicationFactory<Program>();

            var exception = Assert.ThrowsAny<Exception>(() => productionFactory.Server);
            var invalidOperation = exception as InvalidOperationException ?? exception.InnerException as InvalidOperationException;

            Assert.NotNull(invalidOperation);
            Assert.Contains("Production", invalidOperation.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
            Environment.SetEnvironmentVariable("MockMode__Enabled", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__TreasuryServiceOrchestrator", null);
        }
    }
}
