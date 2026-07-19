using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

/// <summary>
/// Ticket 18.3: proves <see cref="ScheduledBalanceSnapshotService.RunOnceAsync"/> writes one
/// <see cref="BalanceSnapshot"/> per fund account against the real DB/handler pipeline, without
/// mutating the fund account's balance — mirrors <see cref="DepositReconciliationIntegrationTests"/>
/// for the fixture pattern (direct-DB seeding, resolve the service from a DI scope, call
/// <c>RunOnceAsync</c> directly rather than through the background service/timer).
/// </summary>
public sealed class ScheduledBalanceSnapshotIntegrationTests(TreasuryServiceOrchestratorApiFactory factory)
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
    public async Task RunOnceAsync_ActiveFundAccount_WritesScheduledSnapshotWithoutMutatingBalance()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var circleWalletId = $"wallet-{Guid.NewGuid():N}";
        var balance = new Money(500m, "USDC");
        using var app = WithMockMode();

        var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
        subAccount.BeginCompliance(circleWalletId);
        subAccount.MarkAccepted();
        var fundAccount = FundAccount.Create(clientCompanyId, balance, DateTime.UtcNow);

        using (var seedScope = app.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            dbContext.SubAccounts.Add(subAccount);
            dbContext.FundAccounts.Add(fundAccount);
            await dbContext.SaveChangesAsync(ct);
        }

        int snapshotCount;
        using (var runScope = app.Services.CreateScope())
        {
            var snapshotService = runScope.ServiceProvider.GetRequiredService<ScheduledBalanceSnapshotService>();
            snapshotCount = await snapshotService.RunOnceAsync(ct);
        }

        // >= 1 rather than exactly 1: the DB factory is shared across both test methods in this
        // class (IClassFixture), so a sibling test's fund accounts may already be present.
        Assert.True(snapshotCount >= 1);

        using var assertScope = app.Services.CreateScope();
        var assertDbContext = assertScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var snapshot = await assertDbContext.BalanceSnapshots
            .SingleOrDefaultAsync(s => s.ClientCompanyId == clientCompanyId, ct);
        Assert.NotNull(snapshot);
        Assert.Equal(BalanceSnapshotReason.Scheduled, snapshot!.Reason);
        Assert.Equal(balance.Amount, snapshot.Balance.Amount);
        Assert.Equal(subAccount.Id, snapshot.SubAccountId);

        var persistedFundAccount = await assertDbContext.FundAccounts
            .SingleOrDefaultAsync(f => f.ClientCompanyId == clientCompanyId, ct);
        Assert.NotNull(persistedFundAccount);
        Assert.Equal(balance.Amount, persistedFundAccount!.Balance.Amount);
    }

    [Fact]
    public async Task RunOnceAsync_MultipleFundAccounts_WritesOneSnapshotPerAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyIdOne = $"client-{Guid.NewGuid():N}";
        var clientCompanyIdTwo = $"client-{Guid.NewGuid():N}";
        var balanceOne = new Money(120m, "USDC");
        var balanceTwo = new Money(340m, "USDC");
        using var app = WithMockMode();

        var subAccountOne = SubAccount.Create(clientCompanyIdOne, DateTime.UtcNow);
        subAccountOne.BeginCompliance($"wallet-{Guid.NewGuid():N}");
        subAccountOne.MarkAccepted();
        var subAccountTwo = SubAccount.Create(clientCompanyIdTwo, DateTime.UtcNow);
        subAccountTwo.BeginCompliance($"wallet-{Guid.NewGuid():N}");
        subAccountTwo.MarkAccepted();

        var fundAccountOne = FundAccount.Create(clientCompanyIdOne, balanceOne, DateTime.UtcNow);
        var fundAccountTwo = FundAccount.Create(clientCompanyIdTwo, balanceTwo, DateTime.UtcNow);

        using (var seedScope = app.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            dbContext.SubAccounts.AddRange(subAccountOne, subAccountTwo);
            dbContext.FundAccounts.AddRange(fundAccountOne, fundAccountTwo);
            await dbContext.SaveChangesAsync(ct);
        }

        // Note: unlike DepositReconciliationIntegrationTests, there's no practical way at the
        // integration level to force one account's snapshot write to fail (e.g. via a DB
        // constraint conflict) while leaving the other's seed data untouched — BalanceSnapshot has
        // no unique constraints to violate. The per-item try/catch isolation (one account's
        // failure must not abort the rest of the pass) is already covered at the unit level in
        // ticket 18.1's ScheduledBalanceSnapshotServiceTests via a mocked repository failure; this
        // test instead proves the happy-path multi-account fan-out end to end against the real DB.
        int snapshotCount;
        using (var runScope = app.Services.CreateScope())
        {
            var snapshotService = runScope.ServiceProvider.GetRequiredService<ScheduledBalanceSnapshotService>();
            snapshotCount = await snapshotService.RunOnceAsync(ct);
        }

        // >= 2 rather than exactly 2: the DB factory is shared across both test methods in this
        // class (IClassFixture), so a sibling test's fund accounts may already be present.
        Assert.True(snapshotCount >= 2);

        using var assertScope = app.Services.CreateScope();
        var assertDbContext = assertScope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var snapshotOne = await assertDbContext.BalanceSnapshots
            .SingleOrDefaultAsync(s => s.ClientCompanyId == clientCompanyIdOne, ct);
        var snapshotTwo = await assertDbContext.BalanceSnapshots
            .SingleOrDefaultAsync(s => s.ClientCompanyId == clientCompanyIdTwo, ct);

        Assert.NotNull(snapshotOne);
        Assert.Equal(BalanceSnapshotReason.Scheduled, snapshotOne!.Reason);
        Assert.Equal(balanceOne.Amount, snapshotOne.Balance.Amount);

        Assert.NotNull(snapshotTwo);
        Assert.Equal(BalanceSnapshotReason.Scheduled, snapshotTwo!.Reason);
        Assert.Equal(balanceTwo.Amount, snapshotTwo.Balance.Amount);
    }
}
