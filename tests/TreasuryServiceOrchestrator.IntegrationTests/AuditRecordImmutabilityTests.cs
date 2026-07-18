using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

public sealed class AuditRecordImmutabilityTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    [Fact]
    public async Task UpdateAgainstAuditRecords_IsRejectedByDbTrigger()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var record = AuditRecord.Create(
            "TestEvent", "TestEntity", Guid.NewGuid().ToString(),
            "{}", $"client-{Guid.NewGuid():N}", "corr-1", DateTime.UtcNow);
        db.AuditRecords.Add(record);
        await db.SaveChangesAsync(ct);

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE AuditRecords SET EventType = 'Tampered' WHERE Id = {0}", [record.Id], ct));

        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAgainstAuditRecords_IsRejectedByDbTrigger()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();

        var record = AuditRecord.Create(
            "TestEvent", "TestEntity", Guid.NewGuid().ToString(),
            "{}", $"client-{Guid.NewGuid():N}", "corr-1", DateTime.UtcNow);
        db.AuditRecords.Add(record);
        await db.SaveChangesAsync(ct);

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync(
                "DELETE FROM AuditRecords WHERE Id = {0}", [record.Id], ct));

        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);

        var stillThere = await db.AuditRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == record.Id, ct);
        Assert.NotNull(stillThere);
    }
}
