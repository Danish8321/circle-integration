using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class RedeemRequestFeesPersistenceTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    [Fact]
    public async Task ZeroFeeAndNetAmount_SurviveSaveAndReloadInFreshScope()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid redeemRequestId;
        using (var writeScope = factory.Services.CreateScope())
        {
            var writeDb = writeScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var redeemRequest = RedeemRequest.Create(
                Guid.NewGuid(), $"client-{Guid.NewGuid():N}", Guid.NewGuid(),
                new Money(50m, "USDC"), "corr-1", DateTime.UtcNow);
            writeDb.RedeemRequests.Add(redeemRequest);
            await writeDb.SaveChangesAsync(ct);

            redeemRequest.Settle(new Money(0m, "USDC"), new Money(50m, "USDC"), DateTime.UtcNow);
            await writeDb.SaveChangesAsync(ct);
            redeemRequestId = redeemRequest.Id;
        }

        using var readScope = factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var reloaded = await readDb.RedeemRequests.FindAsync([redeemRequestId], ct);

        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.Fees);
        Assert.Equal(0m, reloaded.Fees!.Amount);
        Assert.Equal("USDC", reloaded.Fees.CurrencyCode);
        Assert.NotNull(reloaded.NetAmount);
        Assert.Equal(50m, reloaded.NetAmount!.Amount);
        Assert.Equal("USDC", reloaded.NetAmount.CurrencyCode);
    }
}
