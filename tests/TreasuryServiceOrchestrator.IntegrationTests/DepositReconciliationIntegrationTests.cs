using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

/// <summary>
/// Ticket 15.7: proves <see cref="DepositReconciliationService.RunOnceAsync"/> actually self-heals
/// a phantom deposit (a provider-side deposit the webhook path silently missed) end to end, against
/// the real DB and the real handler pipeline — mirrors <see cref="SubAccountRepositoryTests"/> for
/// the fixture pattern and <see cref="TransactionsAndBalancesEndpointsTests"/> for direct-DB seeding.
/// <see cref="IMockProviderDepositLedger"/> is only registered under mock mode (Program.cs), so
/// mock mode is enabled the same way <see cref="MockProviderWiringTests"/> and
/// <see cref="DemoScriptEndToEndTests"/> do.
/// </summary>
public sealed class DepositReconciliationIntegrationTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private WebApplicationFactory<Program> WithMockMode() => factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["MockMode:Enabled"] = "true",
            });
        });
    });

    [Fact]
    public async Task RunOnceAsync_PhantomDeposit_CreditsTransactionAndFundAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var circleWalletId = $"wallet-{Guid.NewGuid():N}";
        var providerReferenceId = $"phantom-{Guid.NewGuid():N}";
        var depositAmount = new Money(250m, "USDC");
        using var app = WithMockMode();

        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        subAccount.BeginCompliance(circleWalletId);
        subAccount.MarkAccepted();

        using (var seedScope = app.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            dbContext.SubAccounts.Add(subAccount);
            await dbContext.SaveChangesAsync(ct);

            var mockLedger = seedScope.ServiceProvider.GetRequiredService<IMockProviderDepositLedger>();
            await mockLedger.SeedAsync(
                new ProviderDepositRecord(
                    providerReferenceId,
                    circleWalletId,
                    "0xdeadbeefdestination",
                    depositAmount,
                    DepositSourceType.OnChain,
                    DateTime.UtcNow),
                ct);
        }

        int healedCount;
        using (var runScope = app.Services.CreateScope())
        {
            var reconciliationService = runScope.ServiceProvider.GetRequiredService<DepositReconciliationService>();
            healedCount = await reconciliationService.RunOnceAsync(ct);
        }

        Assert.Equal(1, healedCount);

        using var assertScope = app.Services.CreateScope();
        var assertDbContext = assertScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var transaction = await assertDbContext.Transactions
            .SingleOrDefaultAsync(t => t.ProviderReferenceId == providerReferenceId, ct);
        Assert.NotNull(transaction);
        Assert.Equal(TransactionType.Deposit, transaction!.Type);
        Assert.Equal(TransactionStatus.Complete, transaction.Status);
        Assert.Equal(depositAmount.Amount, transaction.Amount.Amount);

        var fundAccount = await assertDbContext.FundAccounts
            .SingleOrDefaultAsync(f => f.ClientCompanyId == clientCompanyId, ct);
        Assert.NotNull(fundAccount);
        Assert.Equal(depositAmount.Amount, fundAccount!.Balance.Amount);
    }

    [Fact]
    public async Task RunOnceAsync_SecondPassAfterHealing_SelfHealsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var circleWalletId = $"wallet-{Guid.NewGuid():N}";
        var providerReferenceId = $"phantom-{Guid.NewGuid():N}";
        var depositAmount = new Money(75m, "USDC");
        using var app = WithMockMode();

        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        subAccount.BeginCompliance(circleWalletId);
        subAccount.MarkAccepted();

        using (var seedScope = app.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            dbContext.SubAccounts.Add(subAccount);
            await dbContext.SaveChangesAsync(ct);

            var mockLedger = seedScope.ServiceProvider.GetRequiredService<IMockProviderDepositLedger>();
            await mockLedger.SeedAsync(
                new ProviderDepositRecord(
                    providerReferenceId,
                    circleWalletId,
                    "0xdeadbeefdestination",
                    depositAmount,
                    DepositSourceType.OnChain,
                    DateTime.UtcNow),
                ct);
        }

        using (var firstPassScope = app.Services.CreateScope())
        {
            var reconciliationService = firstPassScope.ServiceProvider
                .GetRequiredService<DepositReconciliationService>();
            var firstPassHealedCount = await reconciliationService.RunOnceAsync(ct);
            Assert.Equal(1, firstPassHealedCount);
        }

        int secondPassHealedCount;
        using (var secondPassScope = app.Services.CreateScope())
        {
            var reconciliationService = secondPassScope.ServiceProvider
                .GetRequiredService<DepositReconciliationService>();
            secondPassHealedCount = await reconciliationService.RunOnceAsync(ct);
        }

        Assert.Equal(0, secondPassHealedCount);

        using var assertScope = app.Services.CreateScope();
        var assertDbContext = assertScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var transactionCount = await assertDbContext.Transactions
            .CountAsync(t => t.ProviderReferenceId == providerReferenceId, ct);
        Assert.Equal(1, transactionCount);

        var fundAccount = await assertDbContext.FundAccounts
            .SingleOrDefaultAsync(f => f.ClientCompanyId == clientCompanyId, ct);
        Assert.NotNull(fundAccount);
        Assert.Equal(depositAmount.Amount, fundAccount!.Balance.Amount);
    }
}
