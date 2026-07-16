# Deposit Reconciliation Job Implementation Plan
 
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
 
**Goal:** Add a polling background job that lists recent provider-side deposits per active sub-account and self-heals any that never produced a webhook, by reusing the existing `ProcessDepositCommandHandler` credit path.
 
**Architecture:** `DepositReconciliationBackgroundService` (Infrastructure `BackgroundService`, patterned exactly on `NotificationDispatchBackgroundService`) polls every `ReconciliationOptions.IntervalSeconds`, each pass opening a fresh DI scope and calling `DepositReconciliationService.RunOnceAsync` (Application). That service lists active sub-accounts with a wallet id, asks `ISubAccountGateway.ListRecentDepositsAsync` for each, and for every provider record with no matching `Transaction.ProviderReferenceId` invokes `ProcessDepositCommandHandler` — the same handler the webhook path uses, so the existing unique index on `ProviderReferenceId` is the dedup safety net. Mock-mode testability comes from a new in-memory `IMockProviderDepositLedger` singleton that `MockSubAccountGateway.ListRecentDepositsAsync` delegates to, with a `SeedAsync` test-only entry point to inject a "phantom" provider deposit.
 
**Tech Stack:** .NET 10, EF Core, xUnit v3 (Microsoft Testing Platform runner), no new NuGet packages.
 
## Global Constraints
 
- `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true` — do not override per-project.
- Central Package Management: no new package versions needed for this plan; do not add any `Version=` attribute outside `Directory.Packages.props`.
- No MediatR — new handler-shaped work uses `ICommandHandler<TCmd,TResult>` / `IQueryHandler<TQ,TResult>` only (this plan adds no new command/query handler, but `DepositReconciliationService` calls the existing `ICommandHandler<ProcessDepositCommand, ProcessDepositResult>`).
- Every async test call must use `TestContext.Current.CancellationToken` (xUnit1051 is a build error).
- `[Collection("HostBuilding")]` only on tests that build `WebApplicationFactory<Program>`.
- Iteration-level and per-subaccount resilience: the poll loop and the per-subaccount inner loop must each catch all non-cancellation exceptions, log via `ILogger`, and continue — never throw out of `ExecuteAsync` or abort the rest of a pass (same hardening as `NotificationDispatchBackgroundService`, applied after Task 13's final review).
- `Money(decimal Amount, string CurrencyCode)` is the only monetary type crossing Domain/Application boundaries.
- Correlation id for self-healed transactions: literally `reconciliation-{providerReferenceId}` (no other format).
- Structured-log-only alerting for unresolvable mismatches — no new notification-outbox integration, no new alerting infra.
- Out of scope (do not build): transfer/payout reconciliation, stale-`PendingCompliance` polling, amount/status divergence detection on already-recorded deposits, notification-outbox integration for unresolvable mismatches. These stay in `TODOS.md`.
---
 
## File Structure
 
New files:
- `src/TreasuryServiceOrchestrator.Application/Ports/IMockProviderDepositLedger.cs` — port interface (lives in Application so both the mock gateway in Infrastructure and its tests can depend on it without a circular reference).
- `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderDepositLedger.cs` — in-memory implementation.
- `src/TreasuryServiceOrchestrator.Application/Ports/GatewayDtos.cs` — add `ProviderDepositRecord` record (existing file, append).
- `src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountGateway.cs` — add `ListRecentDepositsAsync` method (existing file, modify).
- `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs` — implement the new method by delegating to the ledger (existing file, modify).
- `src/TreasuryServiceOrchestrator.Infrastructure/Circle/CircleSubAccountGateway.cs` — implement the new method as an empty-list stub, matching this file's existing stub convention (existing file, modify).
- `src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountRepository.cs` — add `ListActiveWithWalletAsync` (existing file, modify).
- `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/SubAccountRepository.cs` — implement it (existing file, modify).
- `src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/ReconciliationOptions.cs` — options class.
- `src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/DepositReconciliationService.cs` — the reconciliation pass logic.
- `src/TreasuryServiceOrchestrator.Infrastructure/Reconciliation/DepositReconciliationBackgroundService.cs` — polling `BackgroundService`.
- `src/TreasuryServiceOrchestrator.Api/appsettings.json` — add `Reconciliation` config section (existing file, modify).
- `src/TreasuryServiceOrchestrator.Api/Program.cs` — DI wiring (existing file, modify).
- `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockProviderDepositLedgerTests.cs`
- `tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/Reconciliation/DepositReconciliationServiceTests.cs`
- `tests/TreasuryServiceOrchestrator.IntegrationTests/DepositReconciliationIntegrationTests.cs`
---
 
### Task 1: Mock provider deposit ledger
 
**Files:**
- Create: `src/TreasuryServiceOrchestrator.Application/Ports/IMockProviderDepositLedger.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ports/GatewayDtos.cs` (append `ProviderDepositRecord`)
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderDepositLedger.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockProviderDepositLedgerTests.cs`
**Interfaces:**
- Produces: `ProviderDepositRecord(string ProviderReferenceId, string CircleWalletId, string DestinationAddress, Money Amount, DateTime OccurredAtUtc)` in `TreasuryServiceOrchestrator.Application.Ports`.
- Produces: `IMockProviderDepositLedger` with `SeedAsync(string circleWalletId, Money amount, DateTime occurredAtUtc, CancellationToken cancellationToken)` and `Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken)`.
- Produces: `MockProviderDepositLedger : IMockProviderDepositLedger`, registered as a DI singleton (wired in Task 6).
- [ ] **Step 1: Add the `ProviderDepositRecord` DTO**
Append to `src/TreasuryServiceOrchestrator.Application/Ports/GatewayDtos.cs` (end of file, after `CreateTransferGatewayResult`):
 
```csharp
public sealed record ProviderDepositRecord(
    string ProviderReferenceId,
    string CircleWalletId,
    string DestinationAddress,
    Money Amount,
    DateTime OccurredAtUtc);
```
 
- [ ] **Step 2: Write the port interface**
Create `src/TreasuryServiceOrchestrator.Application/Ports/IMockProviderDepositLedger.cs`:
 
```csharp
using TreasuryServiceOrchestrator.Domain;
 
namespace TreasuryServiceOrchestrator.Application.Ports;
 
public interface IMockProviderDepositLedger
{
    Task SeedAsync(string circleWalletId, Money amount, DateTime occurredAtUtc, CancellationToken cancellationToken);
 
    Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken);
}
```
 
- [ ] **Step 3: Write the failing unit tests**
Create `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockProviderDepositLedgerTests.cs`:
 
```csharp
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;
 
namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;
 
public sealed class MockProviderDepositLedgerTests
{
    [Fact]
    public async Task Seeded_entry_is_returned_by_list_when_within_window()
    {
        var ledger = new MockProviderDepositLedger();
        var occurredAtUtc = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
 
        await ledger.SeedAsync("wallet-1", new Money(100m, "USD"), occurredAtUtc, TestContext.Current.CancellationToken);
 
        var results = await ledger.ListRecentDepositsAsync(
            "wallet-1", occurredAtUtc.AddMinutes(-5), TestContext.Current.CancellationToken);
 
        var record = Assert.Single(results);
        Assert.Equal("wallet-1", record.CircleWalletId);
        Assert.Equal(new Money(100m, "USD"), record.Amount);
        Assert.Equal(occurredAtUtc, record.OccurredAtUtc);
    }
 
    [Fact]
    public async Task Entries_outside_the_lookback_window_are_excluded()
    {
        var ledger = new MockProviderDepositLedger();
        var occurredAtUtc = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
 
        await ledger.SeedAsync("wallet-2", new Money(50m, "USD"), occurredAtUtc, TestContext.Current.CancellationToken);
 
        var results = await ledger.ListRecentDepositsAsync(
            "wallet-2", occurredAtUtc.AddMinutes(1), TestContext.Current.CancellationToken);
 
        Assert.Empty(results);
    }
 
    [Fact]
    public async Task List_only_returns_entries_for_the_requested_wallet()
    {
        var ledger = new MockProviderDepositLedger();
        var occurredAtUtc = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
 
        await ledger.SeedAsync("wallet-a", new Money(10m, "USD"), occurredAtUtc, TestContext.Current.CancellationToken);
        await ledger.SeedAsync("wallet-b", new Money(20m, "USD"), occurredAtUtc, TestContext.Current.CancellationToken);
 
        var results = await ledger.ListRecentDepositsAsync(
            "wallet-a", occurredAtUtc.AddMinutes(-5), TestContext.Current.CancellationToken);
 
        var record = Assert.Single(results);
        Assert.Equal("wallet-a", record.CircleWalletId);
    }
}
```
 
- [ ] **Step 4: Run tests to verify they fail**
Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockProviderDepositLedgerTests*"`
Expected: FAIL (build error — `MockProviderDepositLedger` does not exist yet).
 
- [ ] **Step 5: Implement `MockProviderDepositLedger`**
Create `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderDepositLedger.cs`:
 
```csharp
using System.Collections.Concurrent;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Domain;
 
namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;
 
public sealed class MockProviderDepositLedger : IMockProviderDepositLedger
{
    private readonly ConcurrentBag<ProviderDepositRecord> _entries = new();
 
    public Task SeedAsync(string circleWalletId, Money amount, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var record = new ProviderDepositRecord(
            ProviderReferenceId: $"mock-deposit-{Guid.NewGuid():N}",
            CircleWalletId: circleWalletId,
            DestinationAddress: $"0x{Guid.NewGuid():N}",
            Amount: amount,
            OccurredAtUtc: occurredAtUtc);
 
        _entries.Add(record);
        return Task.CompletedTask;
    }
 
    public Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderDepositRecord> matches = _entries
            .Where(e => e.CircleWalletId == circleWalletId && e.OccurredAtUtc >= sinceUtc)
            .ToList();
 
        return Task.FromResult(matches);
    }
}
```
 
- [ ] **Step 6: Run tests to verify they pass**
Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockProviderDepositLedgerTests*"`
Expected: PASS (3/3).
 
- [ ] **Step 7: Build and commit**
```bash
dotnet build
git add src/TreasuryServiceOrchestrator.Application/Ports/GatewayDtos.cs src/TreasuryServiceOrchestrator.Application/Ports/IMockProviderDepositLedger.cs src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderDepositLedger.cs tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockProviderDepositLedgerTests.cs
git commit -m "feat: add in-memory mock provider deposit ledger"
```
 
---
 
### Task 2: `ISubAccountGateway.ListRecentDepositsAsync` — wire mock and Circle gateways
 
**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Circle/CircleSubAccountGateway.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockSubAccountGatewayTests.cs` (create if it doesn't already exist, otherwise append to it — check with `Glob` for `**/MockSubAccountGatewayTests.cs` first)
**Interfaces:**
- Consumes: `IMockProviderDepositLedger` (Task 1), `ProviderDepositRecord` (Task 1).
- Produces: `ISubAccountGateway.ListRecentDepositsAsync(string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken)`, implemented by both `MockSubAccountGateway` and `CircleSubAccountGateway`. `DepositReconciliationService` (Task 5) calls this.
- [ ] **Step 1: Add the method to the port interface**
Edit `src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountGateway.cs` — add after `RegisterRecipientAsync`:
 
```csharp
    Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken);
```
 
Full resulting file:
 
```csharp
namespace TreasuryServiceOrchestrator.Application.Ports;
 
public interface ISubAccountGateway
{
    Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken);
 
    Task<ExternalEntityStatusResult> GetExternalEntityAsync(
        string walletId, CancellationToken cancellationToken);
 
    Task<GenerateDepositAddressResult> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken cancellationToken);
 
    Task<RegisterRecipientGatewayResult> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken cancellationToken);
 
    Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken);
}
```
 
- [ ] **Step 2: Build to confirm the two implementers now fail to compile**
Run: `dotnet build`
Expected: FAIL — `MockSubAccountGateway` and `CircleSubAccountGateway` do not implement `ISubAccountGateway.ListRecentDepositsAsync`.
 
- [ ] **Step 3: Write the failing test for the mock gateway delegating to the ledger**
Check first whether `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockSubAccountGatewayTests.cs` already exists (`Glob` for it). If it exists, append this test method inside the existing test class. If it does not exist, create it with this content:
 
```csharp
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;
 
namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;
 
public sealed class MockSubAccountGatewayTests
{
    [Fact]
    public async Task ListRecentDepositsAsync_delegates_to_the_mock_provider_deposit_ledger()
    {
        var ledger = new MockProviderDepositLedger();
        var occurredAtUtc = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        await ledger.SeedAsync("wallet-1", new Money(100m, "USD"), occurredAtUtc, TestContext.Current.CancellationToken);
 
        var gateway = new MockSubAccountGateway(
            Options.Create(new MockProviderOptions()),
            new NoOpMockWebhookScheduler(),
            new FixedMockRandomSource(0d),
            ledger);
 
        var results = await gateway.ListRecentDepositsAsync(
            "wallet-1", occurredAtUtc.AddMinutes(-5), TestContext.Current.CancellationToken);
 
        var record = Assert.Single(results);
        Assert.Equal("wallet-1", record.CircleWalletId);
    }
}
```
 
If this is a new file, it needs the two small test doubles used above. Check first whether `NoOpMockWebhookScheduler` and `FixedMockRandomSource` already exist anywhere under `tests/TreasuryServiceOrchestrator.UnitTests` (`Glob` for `**/NoOpMockWebhookScheduler.cs` and `**/FixedMockRandomSource.cs`, and `Grep` for `class.*MockWebhookScheduler` and `class.*MockRandomSource` under `tests/`). If either already exists, reuse it (adjust the `using` in the test above to its actual namespace) instead of creating a duplicate. Only if neither exists, add this file:
 
`tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockGatewayTestDoubles.cs`:
 
```csharp
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
 
namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;
 
public sealed class NoOpMockWebhookScheduler : IMockWebhookScheduler
{
    public void Schedule(ScheduledMockWebhook webhook)
    {
    }
}
 
public sealed class FixedMockRandomSource(double value) : IMockRandomSource
{
    public double NextDouble() => value;
}
```
 
(Before adding this file, `Read` `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/IMockWebhookScheduler.cs` and `IMockRandomSource.cs` to confirm the exact interface member signatures — `Schedule` and `NextDouble` are the members observed via `MockSubAccountGateway`'s usage in Task 1's context-gathering; confirm before writing this file so it compiles on the first try.)
 
- [ ] **Step 4: Run the test to verify it fails**
Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockSubAccountGatewayTests*"`
Expected: FAIL (build error — constructor doesn't accept a 4th parameter yet, method doesn't exist).
 
- [ ] **Step 5: Implement `MockSubAccountGateway.ListRecentDepositsAsync`**
Edit `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs` — add `IMockProviderDepositLedger depositLedger` to the primary constructor parameter list, and add the new method. Resulting class shape:
 
```csharp
public sealed class MockSubAccountGateway(
    IOptions<MockProviderOptions> options,
    IMockWebhookScheduler webhookScheduler,
    IMockRandomSource randomSource,
    IMockProviderDepositLedger depositLedger) : ISubAccountGateway
{
    // ... existing members unchanged ...
 
    public Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken)
        => depositLedger.ListRecentDepositsAsync(circleWalletId, sinceUtc, cancellationToken);
}
```
 
- [ ] **Step 6: Implement the `CircleSubAccountGateway` stub**
Edit `src/TreasuryServiceOrchestrator.Infrastructure/Circle/CircleSubAccountGateway.cs` — add, matching this file's existing stub convention (fake-success return, not `NotImplementedException` — every other method in this class already follows that pattern):
 
```csharp
    public Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ProviderDepositRecord>>([]);
```
 
Real HTTP integration (`GET /v1/businessAccount/deposits?walletId=`) lands in Phase 3.
 
- [ ] **Step 7: Run tests to verify they pass**
Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockSubAccountGatewayTests*"`
Expected: PASS.
 
Run: `dotnet build`
Expected: 0 warnings, 0 errors.
 
- [ ] **Step 8: Commit**
```bash
git add src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountGateway.cs src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs src/TreasuryServiceOrchestrator.Infrastructure/Circle/CircleSubAccountGateway.cs tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockSubAccountGatewayTests.cs
git add tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockGatewayTestDoubles.cs 2>/dev/null || true
git commit -m "feat: add ListRecentDepositsAsync to ISubAccountGateway"
```
 
---
 
### Task 3: `ISubAccountRepository.ListActiveWithWalletAsync`
 
**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/SubAccountRepository.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/SubAccountRepositoryTests.cs` (create if it doesn't already exist — `Glob` for it first; if it exists, append the test method to the existing class)
**Interfaces:**
- Consumes: `SubAccount` (`Domain`), fields `LifecycleState` (`SubAccountLifecycleState`), `IsDisabled` (`bool`), `CircleWalletId` (`string?`).
- Produces: `ISubAccountRepository.ListActiveWithWalletAsync(CancellationToken cancellationToken)` returning `Task<IReadOnlyList<SubAccount>>`. `DepositReconciliationService` (Task 5) calls this.
- [ ] **Step 1: Add the method to the port interface**
Edit `src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountRepository.cs`, add:
 
```csharp
    Task<IReadOnlyList<SubAccount>> ListActiveWithWalletAsync(CancellationToken cancellationToken);
```
 
Full resulting file:
 
```csharp
using TreasuryServiceOrchestrator.Domain;
 
namespace TreasuryServiceOrchestrator.Application.Ports;
 
public interface ISubAccountRepository
{
    Task AddAsync(SubAccount subAccount, CancellationToken cancellationToken);
    Task<SubAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SubAccount?> GetByCircleWalletIdAsync(string walletId, CancellationToken cancellationToken);
    Task<SubAccount?> GetByClientCompanyIdAsync(string clientCompanyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubAccount>> ListAsync(SubAccountLifecycleState? stateFilter, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubAccount>> ListActiveWithWalletAsync(CancellationToken cancellationToken);
}
```
 
- [ ] **Step 2: Write the failing integration test**
If `tests/TreasuryServiceOrchestrator.IntegrationTests/SubAccountRepositoryTests.cs` does not exist, create it. Read `tests/TreasuryServiceOrchestrator.IntegrationTests/Support/SqlServerTestDatabaseFixture.cs` first to confirm its exact constructor/`InitializeAsync`/`ConnectionString` shape (used identically by `NotificationOutboxDeliveryTests` in Task 5/6 for reference) before writing this test, then:
 
```csharp
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;
 
namespace TreasuryServiceOrchestrator.IntegrationTests;
 
public sealed class SubAccountRepositoryTests : IAsyncLifetime
{
    private readonly SqlServerTestDatabaseFixture _database = new();
 
    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
 
        var options = new DbContextOptionsBuilder<TreasuryServiceOrchestratorDbContext>()
            .UseSqlServer(_database.ConnectionString)
            .Options;
        await using var context = new TreasuryServiceOrchestratorDbContext(options, new TestTenantContext(null));
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }
 
    public async ValueTask DisposeAsync() => await _database.DisposeAsync();
 
    private TreasuryServiceOrchestratorDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TreasuryServiceOrchestratorDbContext>()
            .UseSqlServer(_database.ConnectionString)
            .Options;
        return new TreasuryServiceOrchestratorDbContext(options, new TestTenantContext(null));
    }
 
    [Fact]
    public async Task ListActiveWithWalletAsync_excludes_inactive_disabled_and_walletless_sub_accounts()
    {
        await using (var seedContext = CreateContext())
        {
            seedContext.SubAccounts.AddRange(
                new SubAccount
                {
                    Id = Guid.NewGuid(), ClientCompanyId = "acme-active", CircleWalletId = "wallet-active",
                    LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                },
                new SubAccount
                {
                    Id = Guid.NewGuid(), ClientCompanyId = "acme-disabled", CircleWalletId = "wallet-disabled",
                    LifecycleState = SubAccountLifecycleState.Active, IsDisabled = true,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                },
                new SubAccount
                {
                    Id = Guid.NewGuid(), ClientCompanyId = "acme-pending", CircleWalletId = "wallet-pending",
                    LifecycleState = SubAccountLifecycleState.PendingCompliance, IsDisabled = false,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                },
                new SubAccount
                {
                    Id = Guid.NewGuid(), ClientCompanyId = "acme-nowallet", CircleWalletId = null,
                    LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                });
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
 
        await using var context = CreateContext();
        var repository = new SubAccountRepository(context);
 
        var results = await repository.ListActiveWithWalletAsync(TestContext.Current.CancellationToken);
 
        var result = Assert.Single(results);
        Assert.Equal("acme-active", result.ClientCompanyId);
    }
}
```
 
Check `SubAccountLifecycleState`'s exact member names first (`Grep -n "enum SubAccountLifecycleState" -A 10 src/TreasuryServiceOrchestrator.Domain`) — `PendingCompliance` is used elsewhere in this codebase per prior task summaries; confirm the exact casing before relying on it here.
 
- [ ] **Step 3: Run test to verify it fails**
Run: `dotnet test tests/TreasuryServiceOrchestrator.IntegrationTests -- --filter-class "*SubAccountRepositoryTests*"`
Expected: FAIL (build error — `ListActiveWithWalletAsync` not implemented).
 
- [ ] **Step 4: Implement the repository method**
Edit `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/SubAccountRepository.cs`, add:
 
```csharp
    public async Task<IReadOnlyList<SubAccount>> ListActiveWithWalletAsync(CancellationToken cancellationToken)
        => await context.SubAccounts
            .Where(s => s.LifecycleState == SubAccountLifecycleState.Active
                        && !s.IsDisabled
                        && s.CircleWalletId != null)
            .ToListAsync(cancellationToken);
```
 
- [ ] **Step 5: Run test to verify it passes**
Run: `dotnet test tests/TreasuryServiceOrchestrator.IntegrationTests -- --filter-class "*SubAccountRepositoryTests*"`
Expected: PASS. Requires SQL Server LocalDB (per `CLAUDE.md`).
 
- [ ] **Step 6: Build and commit**
```bash
dotnet build
git add src/TreasuryServiceOrchestrator.Application/Ports/ISubAccountRepository.cs src/TreasuryServiceOrchestrator.Infrastructure/Persistence/SubAccountRepository.cs tests/TreasuryServiceOrchestrator.IntegrationTests/SubAccountRepositoryTests.cs
git commit -m "feat: add ListActiveWithWalletAsync to ISubAccountRepository"
```
 
---
 
### Task 4: `ReconciliationOptions` + config
 
**Files:**
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/ReconciliationOptions.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.json`
**Interfaces:**
- Produces: `ReconciliationOptions { int IntervalSeconds = 300; int LookbackWindowMinutes = 1440; }` in `TreasuryServiceOrchestrator.Application.Ledger.Reconciliation`. Consumed by `DepositReconciliationService` (Task 5) and `DepositReconciliationBackgroundService` (Task 6), and configured in `Program.cs` (Task 6).
- [ ] **Step 1: Write the options class**
Create `src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/ReconciliationOptions.cs`:
 
```csharp
namespace TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;
 
public sealed class ReconciliationOptions
{
    public int IntervalSeconds { get; set; } = 300;
 
    public int LookbackWindowMinutes { get; set; } = 1440;
}
```
 
- [ ] **Step 2: Add the config section**
Edit `src/TreasuryServiceOrchestrator.Api/appsettings.json` — add a `Reconciliation` section after `Notifications` (before the closing `}`):
 
```json
  "Notifications": {
    "EndpointUrl": "http://localhost:5080/internal/notifications",
    "MaxBatchSize": 20,
    "PollingIntervalMilliseconds": 500,
    "BaseBackoffMilliseconds": 1000,
    "MaxBackoffMilliseconds": 60000
  },
  "Reconciliation": {
    "IntervalSeconds": 300,
    "LookbackWindowMinutes": 1440
  }
```
 
- [ ] **Step 3: Build**
Run: `dotnet build`
Expected: 0 warnings, 0 errors (this task adds a plain options class with no consumer yet — nothing to test in isolation).
 
- [ ] **Step 4: Commit**
```bash
git add src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/ReconciliationOptions.cs src/TreasuryServiceOrchestrator.Api/appsettings.json
git commit -m "feat: add ReconciliationOptions and config section"
```
 
---
 
### Task 5: `DepositReconciliationService`
 
**Files:**
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/DepositReconciliationService.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/Reconciliation/DepositReconciliationServiceTests.cs`
**Interfaces:**
- Consumes: `ISubAccountRepository.ListActiveWithWalletAsync` (Task 3), `ISubAccountGateway.ListRecentDepositsAsync` (Task 2), `ITransactionRepository.GetByProviderReferenceIdAsync(string providerReferenceId, CancellationToken cancellationToken = default)` (existing), `ICommandHandler<ProcessDepositCommand, ProcessDepositResult>` (existing, `HandleAsync(ProcessDepositCommand, CancellationToken)`), `ProcessDepositCommand(string? CircleWalletId, string? DestinationAddress, string CircleReferenceId, DepositSourceType SourceType, Money Amount, string CorrelationId)` (existing), `ReconciliationOptions` (Task 4).
- Produces: `DepositReconciliationService.RunOnceAsync(CancellationToken cancellationToken)` returning `Task<int>` (count of transactions self-healed this pass). Consumed by `DepositReconciliationBackgroundService` (Task 6).
- [ ] **Step 1: Write the failing unit tests**
Create `tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/Reconciliation/DepositReconciliationServiceTests.cs`. This test file needs three in-file fakes (`FakeSubAccountRepository`, `FakeSubAccountGateway`, `FakeTransactionRepository`, `FakeProcessDepositHandler`) since the handler under test only needs the four narrow members it calls, not the full repository/gateway interfaces' entire surface wired through a mocking library (this codebase has no mocking-library dependency in `Directory.Packages.props` — check with `Grep -n "Moq\|NSubstitute\|FakeItEasy" Directory.Packages.props` to confirm before assuming otherwise; if truly absent, hand-written fakes are correct here, matching this codebase's existing test style seen in `ProcessExternalEntityDecisionHandlerTests.cs`):
 
```csharp
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
 
namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger.Reconciliation;
 
file sealed class FakeSubAccountRepository : ISubAccountRepository
{
    public List<SubAccount> ActiveWithWallet { get; } = [];
 
    public Task AddAsync(SubAccount subAccount, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<SubAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<SubAccount?> GetByCircleWalletIdAsync(string walletId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<SubAccount?> GetByClientCompanyIdAsync(string clientCompanyId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<SubAccount>> ListAsync(SubAccountLifecycleState? stateFilter, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<SubAccount>> ListActiveWithWalletAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SubAccount>>(ActiveWithWallet);
}
 
file sealed class FakeSubAccountGateway : ISubAccountGateway
{
    public Dictionary<string, IReadOnlyList<ProviderDepositRecord>> DepositsByWallet { get; } = new();
    public Exception? ThrowOnListFor { get; set; }
 
    public Task<CreateExternalEntityResult> CreateExternalEntityAsync(CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ExternalEntityStatusResult> GetExternalEntityAsync(string walletId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<GenerateDepositAddressResult> GenerateDepositAddressAsync(GenerateDepositAddressGatewayRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<RegisterRecipientGatewayResult> RegisterRecipientAsync(RegisterRecipientGatewayRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
 
    public Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        if (ThrowOnListFor is not null)
        {
            throw ThrowOnListFor;
        }
 
        return Task.FromResult(
            DepositsByWallet.TryGetValue(circleWalletId, out var deposits)
                ? deposits
                : (IReadOnlyList<ProviderDepositRecord>)[]);
    }
}
 
file sealed class FakeTransactionRepository : ITransactionRepository
{
    public HashSet<string> ExistingProviderReferenceIds { get; } = [];
 
    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Transaction>> ListAsync(Guid subAccountId, TransactionType? type, TransactionStatus? status, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Transaction>> ListAllAsync(string? clientCompanyId, TransactionType? type, TransactionStatus? status, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
 
    public Task<Transaction?> GetByProviderReferenceIdAsync(string providerReferenceId, CancellationToken cancellationToken = default)
        => Task.FromResult(ExistingProviderReferenceIds.Contains(providerReferenceId)
            ? new Transaction
            {
                Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), ClientCompanyId = "existing",
                Type = TransactionType.Deposit, Status = TransactionStatus.Complete,
                Amount = new Money(1m, "USD"), ProviderReferenceId = providerReferenceId,
                DepositSourceType = DepositSourceType.OnChain, CorrelationId = "existing",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            }
            : null);
}
 
file sealed class FakeProcessDepositHandler : ICommandHandler<ProcessDepositCommand, ProcessDepositResult>
{
    public List<ProcessDepositCommand> Invocations { get; } = [];
    public Exception? ThrowOnHandle { get; set; }
 
    public Task<ProcessDepositResult> HandleAsync(ProcessDepositCommand command, CancellationToken cancellationToken = default)
    {
        Invocations.Add(command);
        if (ThrowOnHandle is not null)
        {
            throw ThrowOnHandle;
        }
 
        return Task.FromResult(new ProcessDepositResult(Guid.NewGuid(), TransactionStatus.Complete, command.Amount));
    }
}
 
public sealed class DepositReconciliationServiceTests
{
    private static SubAccount ActiveSubAccount(string walletId) => new()
    {
        Id = Guid.NewGuid(), ClientCompanyId = "acme-co", CircleWalletId = walletId,
        LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };
 
    private static DepositReconciliationService CreateService(
        FakeSubAccountRepository subAccounts, FakeSubAccountGateway gateway,
        FakeTransactionRepository transactions, FakeProcessDepositHandler handler)
        => new(subAccounts, gateway, transactions, handler,
            Options.Create(new ReconciliationOptions()), NullLogger<DepositReconciliationService>.Instance);
 
    [Fact]
    public async Task No_active_sub_accounts_is_a_no_op()
    {
        var subAccounts = new FakeSubAccountRepository();
        var gateway = new FakeSubAccountGateway();
        var transactions = new FakeTransactionRepository();
        var handler = new FakeProcessDepositHandler();
        var service = CreateService(subAccounts, gateway, transactions, handler);
 
        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);
 
        Assert.Equal(0, healedCount);
        Assert.Empty(handler.Invocations);
    }
 
    [Fact]
    public async Task Unmatched_provider_deposit_is_credited_via_ProcessDepositCommandHandler()
    {
        var subAccounts = new FakeSubAccountRepository();
        subAccounts.ActiveWithWallet.Add(ActiveSubAccount("wallet-1"));
 
        var gateway = new FakeSubAccountGateway();
        gateway.DepositsByWallet["wallet-1"] =
        [
            new ProviderDepositRecord("provider-ref-1", "wallet-1", "0xabc", new Money(100m, "USD"), DateTime.UtcNow),
        ];
 
        var transactions = new FakeTransactionRepository();
        var handler = new FakeProcessDepositHandler();
        var service = CreateService(subAccounts, gateway, transactions, handler);
 
        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);
 
        Assert.Equal(1, healedCount);
        var invocation = Assert.Single(handler.Invocations);
        Assert.Equal("wallet-1", invocation.CircleWalletId);
        Assert.Equal("provider-ref-1", invocation.CircleReferenceId);
        Assert.Equal(DepositSourceType.OnChain, invocation.SourceType);
        Assert.Equal("reconciliation-provider-ref-1", invocation.CorrelationId);
    }
 
    [Fact]
    public async Task Already_recorded_provider_deposit_is_skipped()
    {
        var subAccounts = new FakeSubAccountRepository();
        subAccounts.ActiveWithWallet.Add(ActiveSubAccount("wallet-1"));
 
        var gateway = new FakeSubAccountGateway();
        gateway.DepositsByWallet["wallet-1"] =
        [
            new ProviderDepositRecord("provider-ref-1", "wallet-1", "0xabc", new Money(100m, "USD"), DateTime.UtcNow),
        ];
 
        var transactions = new FakeTransactionRepository();
        transactions.ExistingProviderReferenceIds.Add("provider-ref-1");
        var handler = new FakeProcessDepositHandler();
        var service = CreateService(subAccounts, gateway, transactions, handler);
 
        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);
 
        Assert.Equal(0, healedCount);
        Assert.Empty(handler.Invocations);
    }
 
    [Fact]
    public async Task Unresolvable_deposit_is_logged_and_does_not_throw_or_abort_the_pass()
    {
        var subAccounts = new FakeSubAccountRepository();
        subAccounts.ActiveWithWallet.Add(ActiveSubAccount("wallet-1"));
        subAccounts.ActiveWithWallet.Add(ActiveSubAccount("wallet-2"));
 
        var gateway = new FakeSubAccountGateway();
        gateway.DepositsByWallet["wallet-1"] =
        [
            new ProviderDepositRecord("provider-ref-1", "wallet-1", "0xabc", new Money(100m, "USD"), DateTime.UtcNow),
        ];
        gateway.DepositsByWallet["wallet-2"] =
        [
            new ProviderDepositRecord("provider-ref-2", "wallet-2", "0xdef", new Money(50m, "USD"), DateTime.UtcNow),
        ];
 
        var transactions = new FakeTransactionRepository();
        var handler = new FakeProcessDepositHandler { ThrowOnHandle = new ConflictException("sub-account not active") };
        var service = CreateService(subAccounts, gateway, transactions, handler);
 
        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);
 
        Assert.Equal(0, healedCount);
        Assert.Equal(2, handler.Invocations.Count);
    }
 
    [Fact]
    public async Task Gateway_failure_for_one_sub_account_does_not_abort_the_rest_of_the_pass()
    {
        var subAccounts = new FakeSubAccountRepository();
        subAccounts.ActiveWithWallet.Add(ActiveSubAccount("wallet-broken"));
        subAccounts.ActiveWithWallet.Add(ActiveSubAccount("wallet-ok"));
 
        var gateway = new FakeSubAccountGateway();
        gateway.DepositsByWallet["wallet-ok"] =
        [
            new ProviderDepositRecord("provider-ref-ok", "wallet-ok", "0xabc", new Money(10m, "USD"), DateTime.UtcNow),
        ];
 
        var transactions = new FakeTransactionRepository();
        var handler = new FakeProcessDepositHandler();
 
        var throwingGateway = new ThrowsForOneWalletGateway(gateway, "wallet-broken");
        var service = new DepositReconciliationService(
            subAccounts, throwingGateway, transactions, handler,
            Options.Create(new ReconciliationOptions()), NullLogger<DepositReconciliationService>.Instance);
 
        var healedCount = await service.RunOnceAsync(TestContext.Current.CancellationToken);
 
        Assert.Equal(1, healedCount);
    }
 
    file sealed class ThrowsForOneWalletGateway(FakeSubAccountGateway inner, string brokenWalletId) : ISubAccountGateway
    {
        public Task<CreateExternalEntityResult> CreateExternalEntityAsync(CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExternalEntityStatusResult> GetExternalEntityAsync(string walletId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GenerateDepositAddressResult> GenerateDepositAddressAsync(GenerateDepositAddressGatewayRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RegisterRecipientGatewayResult> RegisterRecipientAsync(RegisterRecipientGatewayRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
 
        public Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
            string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken)
            => circleWalletId == brokenWalletId
                ? throw new ProviderUnavailableException("simulated gateway failure")
                : inner.ListRecentDepositsAsync(circleWalletId, sinceUtc, cancellationToken);
    }
}
```
 
- [ ] **Step 2: Run tests to verify they fail**
Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*DepositReconciliationServiceTests*"`
Expected: FAIL (build error — `DepositReconciliationService` does not exist yet).
 
- [ ] **Step 3: Implement `DepositReconciliationService`**
Create `src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/DepositReconciliationService.cs`:
 
```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Domain;
 
namespace TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;
 
public sealed class DepositReconciliationService(
    ISubAccountRepository subAccounts,
    ISubAccountGateway gateway,
    ITransactionRepository transactions,
    ICommandHandler<ProcessDepositCommand, ProcessDepositResult> processDeposit,
    IOptions<ReconciliationOptions> options,
    ILogger<DepositReconciliationService> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var lookback = TimeSpan.FromMinutes(options.Value.LookbackWindowMinutes);
        var sinceUtc = DateTime.UtcNow - lookback;
        var healedCount = 0;
 
        var activeSubAccounts = await subAccounts.ListActiveWithWalletAsync(cancellationToken);
 
        foreach (var subAccount in activeSubAccounts)
        {
            IReadOnlyList<ProviderDepositRecord> providerDeposits;
            try
            {
                providerDeposits = await gateway.ListRecentDepositsAsync(
                    subAccount.CircleWalletId!, sinceUtc, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Deposit reconciliation could not list provider deposits for wallet {CircleWalletId}; skipping this sub-account for this pass.",
                    subAccount.CircleWalletId);
                continue;
            }
 
            foreach (var providerDeposit in providerDeposits)
            {
                var existing = await transactions.GetByProviderReferenceIdAsync(
                    providerDeposit.ProviderReferenceId, cancellationToken);
                if (existing is not null)
                {
                    continue;
                }
 
                try
                {
                    await processDeposit.HandleAsync(
                        new ProcessDepositCommand(
                            CircleWalletId: providerDeposit.CircleWalletId,
                            DestinationAddress: providerDeposit.DestinationAddress,
                            CircleReferenceId: providerDeposit.ProviderReferenceId,
                            SourceType: DepositSourceType.OnChain,
                            Amount: providerDeposit.Amount,
                            CorrelationId: $"reconciliation-{providerDeposit.ProviderReferenceId}"),
                        cancellationToken);
                    healedCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex,
                        "Deposit reconciliation could not self-heal provider deposit {ProviderReferenceId} for wallet {CircleWalletId}.",
                        providerDeposit.ProviderReferenceId, providerDeposit.CircleWalletId);
                }
            }
        }
 
        return healedCount;
    }
}
```
 
- [ ] **Step 4: Run tests to verify they pass**
Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*DepositReconciliationServiceTests*"`
Expected: PASS (5/5).
 
- [ ] **Step 5: Build and commit**
```bash
dotnet build
git add src/TreasuryServiceOrchestrator.Application/Ledger/Reconciliation/DepositReconciliationService.cs tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/Reconciliation/DepositReconciliationServiceTests.cs
git commit -m "feat: add DepositReconciliationService"
```
 
---
 
### Task 6: `DepositReconciliationBackgroundService` + DI wiring
 
**Files:**
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Reconciliation/DepositReconciliationBackgroundService.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
**Interfaces:**
- Consumes: `DepositReconciliationService.RunOnceAsync(CancellationToken)` (Task 5), `ReconciliationOptions` (Task 4), `IServiceScopeFactory` (framework), `IMockProviderDepositLedger` (Task 1, registered as singleton here alongside the mock gateway).
- Produces: `DepositReconciliationBackgroundService`, registered via `AddHostedService`.
- [ ] **Step 1: Implement the background service**
Create `src/TreasuryServiceOrchestrator.Infrastructure/Reconciliation/DepositReconciliationBackgroundService.cs`, patterned directly on `src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatchBackgroundService.cs`:
 
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;
 
namespace TreasuryServiceOrchestrator.Infrastructure.Reconciliation;
 
public sealed class DepositReconciliationBackgroundService(
    IServiceScopeFactory scopeFactory, IOptions<ReconciliationOptions> options,
    ILogger<DepositReconciliationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<DepositReconciliationService>();
                await service.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Expected on host shutdown.
            }
            catch (Exception ex)
            {
                // A single failed reconciliation pass must never crash the host (that would take
                // down in-flight, unrelated requests too). Log and retry on the next poll.
                logger.LogError(ex, "Deposit reconciliation pass failed; will retry on next poll.");
            }
 
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Expected on host shutdown.
            }
        }
    }
}
```
 
- [ ] **Step 2: Wire DI in `Program.cs`**
Edit `src/TreasuryServiceOrchestrator.Api/Program.cs`.
 
First, register `IMockProviderDepositLedger` as a singleton alongside the other mock-mode singletons, inside the existing `if (mockProviderOptions.Enabled)` block (around line 79):
 
```csharp
    builder.Services.AddSingleton<TreasuryServiceOrchestrator.Application.Ports.ISubAccountGateway, MockSubAccountGateway>();
    builder.Services.AddSingleton<TreasuryServiceOrchestrator.Application.Ports.IMockProviderDepositLedger,
        TreasuryServiceOrchestrator.Infrastructure.Mocks.MockProviderDepositLedger>();
```
 
`MockProviderDepositLedger` must be registered even outside mock mode for `CircleSubAccountGateway` builds — no, `CircleSubAccountGateway` does not depend on it (Task 2's stub returns an empty list directly), so this registration only needs to be inside the mock-mode branch. Confirm this by re-reading `CircleSubAccountGateway.cs` after Task 2 lands — it must have zero reference to `IMockProviderDepositLedger`.
 
Second, add the reconciliation options + service + hosted-service registrations after the existing Notification wiring block (after line 418, before `var app = builder.Build();`):
 
```csharp
builder.Services.Configure<TreasuryServiceOrchestrator.Application.Ledger.Reconciliation.ReconciliationOptions>(
    builder.Configuration.GetSection("Reconciliation"));
builder.Services.AddScoped<TreasuryServiceOrchestrator.Application.Ledger.Reconciliation.DepositReconciliationService>();
builder.Services.AddHostedService<TreasuryServiceOrchestrator.Infrastructure.Reconciliation.DepositReconciliationBackgroundService>();
```
 
- [ ] **Step 3: Build**
Run: `dotnet build`
Expected: 0 warnings, 0 errors.
 
- [ ] **Step 4: Run the full unit + integration suites to confirm nothing regressed**
Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests`
Expected: PASS, all tests.
 
Run: `dotnet test tests/TreasuryServiceOrchestrator.IntegrationTests`
Expected: PASS, all tests (this exercises `WebApplicationFactory<Program>` host startup, which now includes the new hosted service — a startup wiring mistake here would fail every integration test, not just a new one).
 
- [ ] **Step 5: Commit**
```bash
git add src/TreasuryServiceOrchestrator.Infrastructure/Reconciliation/DepositReconciliationBackgroundService.cs src/TreasuryServiceOrchestrator.Api/Program.cs
git commit -m "feat: add DepositReconciliationBackgroundService and wire it into the host"
```
 
---
 
### Task 7: End-to-end integration test
 
**Files:**
- Create: `tests/TreasuryServiceOrchestrator.IntegrationTests/DepositReconciliationIntegrationTests.cs`
**Interfaces:**
- Consumes: `IMockProviderDepositLedger.SeedAsync` (Task 1), `DepositReconciliationService.RunOnceAsync` (Task 5), the existing `SqlServerTestDatabaseFixture` / `WebApplicationFactory<Program>` pattern (same shape as `NotificationOutboxDeliveryTests`).
- [ ] **Step 1: Write the failing integration test**
Create `tests/TreasuryServiceOrchestrator.IntegrationTests/DepositReconciliationIntegrationTests.cs`, modeled on `NotificationOutboxDeliveryTests.cs`:
 
```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;
 
namespace TreasuryServiceOrchestrator.IntegrationTests;
 
// Seeds a "phantom" provider-side deposit via IMockProviderDepositLedger — one that never produced a
// webhook — then runs one DepositReconciliationService pass directly (not via the background service's
// timer, to keep this test deterministic) and proves the self-heal produced a real Transaction and moved
// the FundAccount balance, exactly the gap this job exists to close.
[Collection("HostBuilding")]
public sealed class DepositReconciliationIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerTestDatabaseFixture _database = new();
    private WebApplicationFactory<Program> _factory = null!;
 
    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _database.ApplyToEnvironment();
 
        var options = new DbContextOptionsBuilder<TreasuryServiceOrchestratorDbContext>()
            .UseSqlServer(_database.ConnectionString)
            .Options;
        await using var context = new TreasuryServiceOrchestratorDbContext(options, new TestTenantContext(null));
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
 
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KnownClientCompanies:0:Id"] = "acme-co",
                    ["KnownClientCompanies:0:Role"] = "SubAccount",
                    ["MockProvider:Enabled"] = "true",
                });
            });
        });
    }
 
    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }
 
    private async Task SeedActiveSubAccountAsync(string circleWalletId)
    {
        var options = new DbContextOptionsBuilder<TreasuryServiceOrchestratorDbContext>()
            .UseSqlServer(_database.ConnectionString)
            .Options;
        await using var context = new TreasuryServiceOrchestratorDbContext(options, new TestTenantContext(null));
 
        context.SubAccounts.Add(new SubAccount
        {
            Id = Guid.NewGuid(),
            ClientCompanyId = "acme-co",
            CircleWalletId = circleWalletId,
            LifecycleState = SubAccountLifecycleState.Active,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
 
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
 
    [Fact]
    public async Task Phantom_provider_deposit_is_self_healed_into_a_transaction_and_fund_account_balance()
    {
        await SeedActiveSubAccountAsync("wallet-recon-1");
 
        using var scope = _factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IMockProviderDepositLedger>();
        await ledger.SeedAsync(
            "wallet-recon-1", new Money(250m, "USD"), DateTime.UtcNow, TestContext.Current.CancellationToken);
 
        var reconciliationService = scope.ServiceProvider.GetRequiredService<DepositReconciliationService>();
        var healedCount = await reconciliationService.RunOnceAsync(TestContext.Current.CancellationToken);
 
        Assert.Equal(1, healedCount);
 
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var transaction = await dbContext.Transactions.AsNoTracking()
            .SingleAsync(t => t.CorrelationId.StartsWith("reconciliation-"), TestContext.Current.CancellationToken);
        Assert.Equal(TransactionStatus.Complete, transaction.Status);
        Assert.Equal(250m, transaction.Amount.Amount);
 
        var fundAccount = await dbContext.FundAccounts.AsNoTracking()
            .SingleAsync(f => f.ClientCompanyId == "acme-co", TestContext.Current.CancellationToken);
        Assert.Equal(250m, fundAccount.Balance);
    }
 
    [Fact]
    public async Task Running_reconciliation_twice_does_not_double_credit_the_same_provider_deposit()
    {
        await SeedActiveSubAccountAsync("wallet-recon-2");
 
        using var scope = _factory.Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IMockProviderDepositLedger>();
        await ledger.SeedAsync(
            "wallet-recon-2", new Money(75m, "USD"), DateTime.UtcNow, TestContext.Current.CancellationToken);
 
        var reconciliationService = scope.ServiceProvider.GetRequiredService<DepositReconciliationService>();
        var firstPassHealed = await reconciliationService.RunOnceAsync(TestContext.Current.CancellationToken);
        var secondPassHealed = await reconciliationService.RunOnceAsync(TestContext.Current.CancellationToken);
 
        Assert.Equal(1, firstPassHealed);
        Assert.Equal(0, secondPassHealed);
    }
}
```
 
- [ ] **Step 2: Run test to verify it fails**
Run: `dotnet test tests/TreasuryServiceOrchestrator.IntegrationTests -- --filter-class "*DepositReconciliationIntegrationTests*"`
Expected: FAIL if any prior task's wiring is incomplete; otherwise this should already PASS on first run since Tasks 1-6 are complete. If it fails, read the failure — do not proceed to Step 3 until the root cause is understood (this test is the acceptance proof for the whole plan).
 
- [ ] **Step 3: Run test to verify it passes**
Run: `dotnet test tests/TreasuryServiceOrchestrator.IntegrationTests -- --filter-class "*DepositReconciliationIntegrationTests*"`
Expected: PASS (2/2).
 
- [ ] **Step 4: Run the full solution test suite**
```bash
dotnet build
dotnet test
dotnet ef migrations has-pending-model-changes --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api
```
 
Expected: 0 warnings on build, all 3 test projects green, "No changes" from the pending-model-changes check (this plan adds no entity properties, so no migration is expected).
 
- [ ] **Step 5: Commit**
```bash
git add tests/TreasuryServiceOrchestrator.IntegrationTests/DepositReconciliationIntegrationTests.cs
git commit -m "test: add end-to-end deposit reconciliation integration test"
```
 
---
 
## Self-Review Notes
 
**Spec coverage:** Architecture (Task 6's background service + Task 5's service), gateway port + mock ledger (Tasks 1-2), repository query (Task 3), configuration (Task 4), error handling/concurrency (per-subaccount and per-deposit try/catch in Task 5, iteration-level try/catch in Task 6), correlation id format (Task 5), testing plan's three named test classes (`DepositReconciliationServiceTests` Task 5, `MockProviderDepositLedgerTests` Task 1, `DepositReconciliationIntegrationTests` Task 7) are all covered. Out-of-scope items are not built anywhere in this plan.
 
**Placeholder scan:** No TBD/TODO markers; every step has complete code.
 
**Type consistency:** `ProviderDepositRecord`, `IMockProviderDepositLedger`, `ReconciliationOptions`, `DepositReconciliationService.RunOnceAsync`, and `ISubAccountRepository.ListActiveWithWalletAsync` use identical signatures across every task that references them.
 
**Known open item folded into this plan:** the design spec (`docs/superpowers/specs/2026-07-14-deposit-reconciliation-design.md`) originally said `CircleSubAccountGateway`'s stub would throw `NotImplementedException`; reading the actual file during this plan's context-gathering showed every existing method on that class returns a fake-success value instead. Task 2 Step 6 follows the actual codebase convention (empty list, no throw) rather than the spec's assumption — call this out explicitly if anyone diffs this plan against the spec.