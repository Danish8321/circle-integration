using Microsoft.Extensions.DependencyInjection;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class SubAccountRepositoryTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    [Fact]
    public async Task ListActiveWithWalletAsync_ExcludesInactiveDisabledAndWalletless()
    {
        var ct = TestContext.Current.CancellationToken;

        var activeWithWallet = SubAccount.Create($"client-{Guid.NewGuid():N}", DateTime.UtcNow);
        activeWithWallet.BeginCompliance($"wallet-{Guid.NewGuid():N}");
        activeWithWallet.MarkAccepted();

        var disabledWithWallet = SubAccount.Create($"client-{Guid.NewGuid():N}", DateTime.UtcNow);
        disabledWithWallet.BeginCompliance($"wallet-{Guid.NewGuid():N}");
        disabledWithWallet.MarkAccepted();
        disabledWithWallet.SetDisabled(true);

        var activeNoWallet = SubAccount.Create($"client-{Guid.NewGuid():N}", DateTime.UtcNow);

        using (var writeScope = factory.Services.CreateScope())
        {
            var writeDb = writeScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            writeDb.SubAccounts.AddRange(activeWithWallet, disabledWithWallet, activeNoWallet);
            await writeDb.SaveChangesAsync(ct);
        }

        using var readScope = factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var repository = new SubAccountRepository(readDb);

        var result = await repository.ListActiveWithWalletAsync(ct);

        var matching = result.Where(x => x.Id == activeWithWallet.Id
            || x.Id == disabledWithWallet.Id
            || x.Id == activeNoWallet.Id).ToList();

        Assert.Single(matching);
        Assert.Equal(activeWithWallet.Id, matching[0].Id);
    }
}
