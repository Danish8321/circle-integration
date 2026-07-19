using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.MsSql;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.IntegrationTests.Webhooks;

/// <summary>
/// Flagged gap from `docs/features/13-internal-notifications-outbox.md` §7/§8 item 1: the
/// same-transaction guarantee between a state-mutating write and its
/// <see cref="NotificationOutboxEntry"/> row must be proven by fault injection, not merely
/// asserted by a positive-path test. No handler wires <c>INotificationOutboxRepository</c> yet
/// (that is ticket 09.5), so this exercises the pattern directly against the shared
/// <see cref="TreasuryServiceOrchestratorDbContext"/>/<c>SaveChangesAsync</c> unit of work that
/// every future call site will ride on.
/// </summary>
public sealed class NotificationOutboxAtomicityTests : IAsyncLifetime
{
    private readonly MsSqlContainer sqlContainer = new MsSqlBuilder().Build();

    public async ValueTask InitializeAsync()
    {
        await sqlContainer.StartAsync();

        var options = new DbContextOptionsBuilder<TreasuryServiceOrchestratorDbContext>()
            .UseSqlServer(sqlContainer.GetConnectionString())
            .Options;
        using var dbContext = new TreasuryServiceOrchestratorDbContext(options, new AdminTestCaller());
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await sqlContainer.DisposeAsync();

    /// <summary>
    /// Throws on the second SQL command sent within a unit of work, simulating a crash/fault
    /// after the first write has reached the database but before the transaction commits.
    /// </summary>
    private sealed class ThrowOnSecondCommandInterceptor : DbCommandInterceptor
    {
        private int commandCount;

        private void ThrowIfSecondCommand()
        {
            if (Interlocked.Increment(ref commandCount) == 2)
            {
                throw new InvalidOperationException("Injected fault: poisoned second command in the unit of work.");
            }
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            ThrowIfSecondCommand();
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSecondCommand();
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            ThrowIfSecondCommand();
            return base.ReaderExecuting(command, eventData, result);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSecondCommand();
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task StateChangeAndOutboxWrite_WhenSaveChangesFailsMidTransaction_PersistsNeither()
    {
        var interceptor = new ThrowOnSecondCommandInterceptor();
        var options = new DbContextOptionsBuilder<TreasuryServiceOrchestratorDbContext>()
            // Force one SQL command per insert (rather than SQL Server provider batching them
            // into a single command) so the interceptor can fail between the two writes.
            .UseSqlServer(sqlContainer.GetConnectionString(), sql => sql.MaxBatchSize(1))
            .AddInterceptors(interceptor)
            .Options;

        var subAccountId = Guid.NewGuid();
        var outboxEntryId = Guid.NewGuid();

        using (var dbContext = new TreasuryServiceOrchestratorDbContext(options, new AdminTestCaller()))
        {
            var subAccount = SubAccount.Create($"client-{Guid.NewGuid():N}", DateTime.UtcNow);
            dbContext.SubAccounts.Add(subAccount);
            subAccountId = subAccount.Id;

            dbContext.NotificationOutboxEntries.Add(new NotificationOutboxEntry
            {
                Id = outboxEntryId,
                EventType = "EntityRegistrationDecided",
                ClientCompanyId = subAccount.ClientCompanyId,
                EntityId = subAccount.Id.ToString(),
                OccurredAtUtc = DateTime.UtcNow,
                CorrelationId = $"corr-{Guid.NewGuid():N}",
                PayloadJson = "{}",
                Status = NotificationDeliveryStatus.Pending,
            });

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        var verifyOptions = new DbContextOptionsBuilder<TreasuryServiceOrchestratorDbContext>()
            .UseSqlServer(sqlContainer.GetConnectionString())
            .Options;
        using var verifyContext = new TreasuryServiceOrchestratorDbContext(verifyOptions, new AdminTestCaller());

        var subAccountPersisted = await verifyContext.SubAccounts.AnyAsync(
            x => x.Id == subAccountId, TestContext.Current.CancellationToken);
        var outboxEntryPersisted = await verifyContext.NotificationOutboxEntries.AnyAsync(
            x => x.Id == outboxEntryId, TestContext.Current.CancellationToken);

        Assert.False(subAccountPersisted, "The state change must not persist when the shared SaveChangesAsync fails.");
        Assert.False(outboxEntryPersisted, "The outbox row must not persist when the shared SaveChangesAsync fails.");
    }

    // This test hand-builds the DbContext outside DI, so it supplies its own caller. Admin bypasses
    // the global tenant query filter (INV7), so the cross-tenant verify reads see every row.
    private sealed class AdminTestCaller : ICallerContext
    {
        public string CallerId => "apiso-admin";
        public CallerRole Role => CallerRole.Admin;
    }
}
