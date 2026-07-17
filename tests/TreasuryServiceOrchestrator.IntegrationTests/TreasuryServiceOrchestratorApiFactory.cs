using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
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
            });
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
