using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Notifications;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class TreasuryServiceOrchestratorApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer sqlContainer = new MsSqlBuilder().Build();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:TreasuryServiceOrchestrator"] = sqlContainer.GetConnectionString(),
                // TestServer's default base address; the notification dispatcher's HttpClient
                // below is rewired to talk to TestServer in-process so it can actually reach the
                // stub receiver controller instead of a real network endpoint.
                ["Notifications:EndpointUrl"] = "http://localhost/internal/notifications",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<INotificationSender, HttpNotificationSender>()
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
        });
    }

    public async ValueTask InitializeAsync()
    {
        await sqlContainer.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        await sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
