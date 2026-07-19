using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

/// <summary>
/// Structural tenant isolation (INV7, audit F7 / ticket 24). Proves the global
/// <c>HasQueryFilter</c> against real SQL Server — the EF filter cannot be exercised at the unit
/// tier. A regular caller sees only its own rows; admin spans tenants; system-context lookups that
/// call <c>IgnoreQueryFilters()</c> resolve any tenant's row regardless of the ambient caller.
/// </summary>
public sealed class TenantIsolationQueryFilterTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private readonly string tenantA = $"client-a-{Guid.NewGuid():N}";
    private readonly string tenantB = $"client-b-{Guid.NewGuid():N}";

    private async Task<(Guid txA, Guid txB, string refB)> SeedTwoTenantsAsync(CancellationToken ct)
    {
        // Writes are not query-filtered, so seeding both tenants under one scope is fine.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var txA = SeedTransaction(db, tenantA, out _);
        var txB = SeedTransaction(db, tenantB, out var refB);
        await db.SaveChangesAsync(ct);
        return (txA, txB, refB);
    }

    private static Guid SeedTransaction(TreasuryServiceOrchestratorDbContext db, string clientCompanyId, out string providerRef)
    {
        providerRef = $"ref-{Guid.NewGuid():N}";
        var transaction = Transaction.Create(
            Guid.NewGuid(),
            clientCompanyId,
            TransactionType.Deposit,
            TransactionStatus.Complete,
            new Money(100m, "USDC"),
            providerRef,
            depositSourceType: null,
            failureReason: null,
            correlationId: $"corr-{Guid.NewGuid():N}",
            nowUtc: DateTime.UtcNow);
        db.Transactions.Add(transaction);
        return transaction.Id;
    }

    private (TreasuryServiceOrchestratorDbContext Db, IServiceScope Scope) ScopeAs(string callerId, CallerRole role)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISettableCallerContext>().Set(callerId, role);
        return (scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>(), scope);
    }

    [Fact]
    public async Task RegularCaller_SeesOnlyOwnTenantRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (txA, txB, _) = await SeedTwoTenantsAsync(ct);

        var (db, scope) = ScopeAs(tenantA, CallerRole.SubAccount);
        using (scope)
        {
            var visible = await db.Transactions
                .Where(x => x.Id == txA || x.Id == txB)
                .Select(x => x.Id)
                .ToListAsync(ct);

            Assert.Contains(txA, visible);
            Assert.DoesNotContain(txB, visible);
        }
    }

    [Fact]
    public async Task Admin_SpansAllTenants()
    {
        var ct = TestContext.Current.CancellationToken;
        var (txA, txB, _) = await SeedTwoTenantsAsync(ct);

        var (db, scope) = ScopeAs("apiso-admin", CallerRole.Admin);
        using (scope)
        {
            var visible = await db.Transactions
                .Where(x => x.Id == txA || x.Id == txB)
                .Select(x => x.Id)
                .ToListAsync(ct);

            Assert.Contains(txA, visible);
            Assert.Contains(txB, visible);
        }
    }

    [Fact]
    public async Task SystemLookup_ResolvesAnyTenant_RegardlessOfCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, txB, refB) = await SeedTwoTenantsAsync(ct);

        // Caller is tenant A, but the provider-id discovery lookup bypasses the filter.
        var (db, scope) = ScopeAs(tenantA, CallerRole.SubAccount);
        using (scope)
        {
            var repository = new TransactionRepository(db);
            var found = await repository.GetByProviderReferenceIdAsync(refB, ct);

            Assert.NotNull(found);
            Assert.Equal(txB, found!.Id);
        }
    }

    [Fact]
    public async Task RegularCaller_CannotReachOtherTenantRow_ViaFilteredQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, _, refB) = await SeedTwoTenantsAsync(ct);

        // Same row, but through a normally-filtered query while acting as tenant A: invisible.
        var (db, scope) = ScopeAs(tenantA, CallerRole.SubAccount);
        using (scope)
        {
            var leaked = await db.Transactions.FirstOrDefaultAsync(x => x.ProviderReferenceId == refB, ct);
            Assert.Null(leaked);
        }
    }
}
