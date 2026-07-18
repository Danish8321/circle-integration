using System.Net.Http;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TreasuryServiceOrchestrator.IntegrationTests;

/// <summary>
/// Proves the named Circle <see cref="HttpClient"/> registrations in Program.cs resolve with
/// <see cref="TreasuryServiceOrchestrator.Infrastructure.Providers.Circle.CircleResiliencePipelineFactory.AddCircleResilienceHandler"/>
/// attached (ticket 17.2). Retry/circuit-breaker *behavior* is covered separately by ticket 17.3.
/// </summary>
public sealed class CircleResilienceWiringTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    [Theory]
    [InlineData("ISubAccountGateway")]
    [InlineData("IStablecoinGateway")]
    public void CircleGatewayClient_ResolvesWithCircleResilienceHandlerAttached(string clientName)
    {
        // Production wires both real Circle gateways (CircleSubAccountGateway, CircleMintGateway)
        // via AddHttpClient<...>().AddCircleResilienceHandler() — see Program.cs's `else` branch.
        using var scope = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
        }).Services.CreateScope();

        var handlerFactory = scope.ServiceProvider.GetRequiredService<IHttpMessageHandlerFactory>();
        using var handler = handlerFactory.CreateHandler(clientName);

        Assert.True(
            ContainsResilienceHandler(handler),
            $"Expected the Circle resilience pipeline to be present in the DelegatingHandler chain for {clientName}.");
    }

    private static bool ContainsResilienceHandler(HttpMessageHandler handler)
    {
        var current = handler;
        while (current is DelegatingHandler delegatingHandler)
        {
            if (delegatingHandler.GetType().FullName?.Contains("Resilience", StringComparison.Ordinal) == true)
            {
                return true;
            }

            current = delegatingHandler.InnerHandler;
        }

        return false;
    }
}
