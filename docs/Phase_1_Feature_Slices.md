# Phase 1 Feature Slices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild TreasuryServiceOrchestrator's feature surface around `docs/PRD.md` — caller/role model, sub-account lifecycle with entity registration, webhook-driven ledger (deposits, transfers, redemptions), recipients, balances, admin cross-tenant views, and an outbox-based internal notifications pipeline — so the running system matches the eleven PRD §15.1 Phase 1 slices end to end.

**Architecture:** Clean/Onion (Domain → Application → Infrastructure → Api), unchanged. Every mutating handler keeps the two-`SaveChangesAsync` idempotency pattern (reserve → gateway/side-effect → complete). All Circle Mint calls stay behind `IStablecoinGateway`/`ISubAccountGateway`; Phase 1 runs those gateways in **mock mode** (in-process fakes + a simulated webhook emitter), since real Circle HTTP integration is Phase 3. Webhooks (real or simulated) land in a durable inbox, get deduped, and are dispatched to per-topic processors that apply state changes and enqueue outbox notifications in the same transaction.

**Tech Stack:** .NET 10, ASP.NET Core Controllers, EF Core 10 (SQL Server / LocalDB), FluentValidation, xUnit v3 on Microsoft Testing Platform, Serilog, OpenTelemetry (optional).

## Module Boundaries (decided 2026-07-16)

Application/Domain code is organized into four named module sub-namespaces, not a flat `Application/Ports` bag:

| Module | Owns | PRD sections |
|---|---|---|
| `Compliance` | SubAccount, EntityRegistration lifecycle | §3, §4 |
| `Ledger` | Wallet, DepositAddress, Transaction, BalanceSnapshot, transfers, redemptions, balances | §6, §7, §8, §9 |
| `Webhooks` | Durable inbox, dedup, per-topic processors, notification outbox | §10, §10.1 |
| `Admin` | Cross-tenant/master-account read views | §2.5 |
| `Shared` | Cross-cutting auth (`ICallerContext`, `TenantScopeResolver`), cross-module provider port (`IStablecoinGateway`), shared config (`SupportedChainsOptions`) | n/a |

Every task below that says `Application/Ports/*.cs` or a flat `Application/Ledger/*.cs` path means "place under `Application/<Module>/Ports/*.cs` or `Application/<Module>/*.cs` per the table above" — e.g. `ISubAccountGateway.cs`/`ISubAccountRepository.cs` → `Application/Compliance/Ports/`, `ITransactionRepository.cs`/`ProcessDepositCommand` → `Application/Ledger/Ports/` and `Application/Ledger/`. Where a type is shared across modules (e.g. `GatewayDtos.cs` if it holds both compliance and ledger DTOs), split it per-module rather than keeping one shared file. Full modular-monolith isolation (separate persistence/deployment per module) is explicitly **not** adopted — see `architecture_module_boundaries` decision; this is namespace/folder discipline only, still one deployable, one `DbContext`.

## Global Constraints

- `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true` (`Directory.Build.props`) — never override per-project.
- Central Package Management: all `Version=` attributes live only in `Directory.Packages.props`.
- **No MediatR.** Every new command/query uses `ICommandHandler<TCmd,TResult>` / `IQueryHandler<TQ,TResult>` with `HandleAsync(TCmd cmd, CancellationToken ct = default)`.
- xUnit v3 on Microsoft Testing Platform — no VSTest. Every async test call passes `TestContext.Current.CancellationToken` (xUnit1051 is a build error, not a lint warning).
- `[Collection("HostBuilding")]` only on tests that build `WebApplicationFactory<Program>`.
- Every mutating handler: idempotency-check → reserve idempotency key (`SaveChangesAsync #1`, atomic via the `(ClientCompanyId, IdempotencyKey)` unique index) → gateway/state-transition → persist + audit + complete idempotency record (`SaveChangesAsync #2`).
- Tenant identity is **always** taken from `ITenantContext`/`ICallerContext`, never from a route or body parameter. Cross-tenant access must be structurally impossible at the data-access layer.
- Admin never impersonates a tenant: it authenticates as itself and names the target scope explicitly; all-tenant access is itself audited.
- Mock mode must be structurally impossible to enable in Production (hard environment check at startup, not just config).
- `Money(decimal Amount, string CurrencyCode)` is the only monetary type crossing Domain/Application boundaries; every Money-typed EF column uses the `ComplexProperty` idiom already established on `RedeemRequest`/`Deposit` (see Task 8).
- `ClientCompanyId` columns use `Latin1_General_100_BIN2` collation (`TreasuryServiceOrchestratorDbContext.ClientCompanyIdCollation`) — case-sensitive tenant isolation.
- Controllers: `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/...")]`, no business logic — dispatch to a handler and return its result.
- RFC 7807 `ProblemDetails` is the only error contract from Task 2 onward; controllers must not catch domain exceptions themselves.
- Outbound transfer commands must **not** attempt to carry FinCEN Travel Rule originator name/address fields — `POST /v1/businessAccount/transfers` has no such request field (PRD §7.3, verified against live Circle docs 2026-07-16); Travel Rule compliance for transfers is satisfied structurally via account-on-file identity + recipient verification (§7.1), not per-call data.
- `dotnet build` must show 0 warnings and `dotnet test` must be fully green after every task's commit.

## Corrections from doc-grilling 2026-07-17 (verified against live Circle docs)

These override any contradicting snippet below. Where Phase 1 code already exists, each is a defect to fix, not just a doc change.

1. **Recipient status literals (Task 9).** `pending_approval` is an invented literal — Circle's REST enum is `pending_verification | verification_succeeded | active`; its webhook vocabulary is `pending | inactive | active | denied`. The status mapper must **not throw on unknown literals**: map `active` → `Active`, `denied` → `Denied`, anything else → `PendingApproval` (log it). Mock gateway/emitter should use the real literals (`pending_verification` on create, webhook `active`/`denied` on decision).
2. **Transfer `running` status (Tasks 6, 10).** Real `transfers` webhooks emit one event per transition: `pending → running → complete | failed`. Mapper already handles `running` (maps to `Pending` per PRD §7.2 — product state machine unchanged); the mock emitter must simulate the `running` intermediate event and support a `failed` outcome so the no-op transition is actually exercised.
3. **Payout net field (Tasks 6, 11).** The real field is **`toAmount`** (not `netAmount`) and it is **optional**. Webhook payload DTO: rename to `ToAmount`, make it nullable, and never throw when absent — net = `toAmount` when present, else computed `amount − fees` (PRD §8). `ProcessPayoutStatusCommand` keeps a non-nullable net, computed at the mapping edge.
4. **Transfer destination shape (Task 10 gateway DTO).** Real request body: `destination: { type: "verified_blockchain", addressId: <recipient UUID> }`. `CreateTransferGatewayRequest.DestinationRecipientId` carries what becomes `addressId` — record this so Phase 3's real client doesn't guess.
5. **On-chain deposits arrive on the `transfers` topic, not `deposits` (Tasks 6, 8).** Circle's `deposits` topic/endpoint is **fiat wire only**; the invented `sourceType` discriminator on the deposits payload does not exist. Correct model: `deposits` webhook → fiat wire deposit (`DepositSourceType.Wire`); `transfers` webhook with `destination` = one of our wallets → on-chain deposit (`DepositSourceType.OnChain`) — the transfers processor must branch on direction: incoming = deposit credit via `ProcessDepositCommand`, outgoing = transfer status update. Mock emitter must emit each on its real topic. (Also fixes the unspecified mock `deposits`-topic emission the Task 14 E2E depends on.)
6. **`wire` webhook topic missing (Task 11).** Linked-bank-account verification is **asynchronous** — the claim that Circle's create endpoint completes verification synchronously is wrong. Real lifecycle arrives on the `wire` topic (`pending → complete | failed`). Add a `wire` topic processor updating `LinkedBankAccountStatus`; mock gateway returns `pending` on create and the emitter schedules the `complete`/`failed` `wire` webhook.
7. **Webhook payload DTOs must match real Circle notification shapes (decided 2026-07-17, all webhook tasks).** The plan's flat payload DTOs (e.g. `CircleDepositWebhookPayload(..., string SourceType, decimal Amount, string Currency)`) are invented. Real deliveries are an SNS `Notification` whose `Message` string contains Circle's envelope `{ clientId, notificationType, version, <resource>: {...} }` with nested money objects carrying **string** amounts (`{"amount": "1000.00", "currency": "USD"}`). The inbox unwraps `Message`; per-topic processors deserialize the real envelope/resource shapes; the mock emitter emits exactly those shapes. This is what makes Phase 3's "pipeline reused unchanged" claim true — see `docs/adr/0007-mock-emits-real-provider-shapes.md`.
8. **`DepositSourceType` members are `Wire | OnChain` (decided 2026-07-17).** `Wire` matches Circle's own literal (`source.type: "wire"`); any `FiatWire` spelling in snippets below is superseded.
9. **Deposit-address generation needs an idempotency key after all (Task 7).** The real `POST /v1/businessAccount/wallets/addresses/deposit` **requires** a body `idempotencyKey` (UUID v4; `walletId` is a query param — verified 2026-07-17). Task 7's find-or-create alone can't survive a crash between the provider call and the local save (retry would mint a second provider address). Fix: `GenerateDepositAddressGatewayRequest` gains `string IdempotencyKey`; the handler reserves a generated key in the existing idempotency table (keyed by `(SubAccountId, Chain, Currency)`-derived scope) before the gateway call and reuses it on retry — the same reserve → call → complete pattern as every other mutating handler, just with a system-generated key instead of a caller-supplied one. The `(SubAccountId, Chain, Currency)` unique index stays as the local dedup.
10. **Chain allow-list cross-check is now done (Task 7 note).** Live chain enum (verified 2026-07-17): `ALGO, APTOS, ARB, ARC, AVAX, BASE, BTC, CELO, CODEX, ETH, HBAR, HYPEREVM, INK, LINEA, NEAR, NOBLE, OP, PLUME, PAH, POLY, SEI, SOL, SONIC, SUI, UNI, WORLDCHAIN, XDC, XLM, XRP, ZKS, ZKSYNC` (`ARC` sandbox-only). Default `["ETH"]` is valid; the allow-list mechanism stands.

---

## Design-pass corrections 2026-07-17 (codebase-design review of all interfaces)

These override any contradicting snippet below, same authority as the doc-grilling corrections above.

1. **`FundAccount.Balance` must be `Money`, not `decimal` (Tasks 8, 10, 11).** The Task 10 consumes-note admitting "`decimal`, not `Money` — see `FundAccount.cs`" is a defect against Global Constraint "`Money` is the only monetary type crossing Domain/Application boundaries", not a fact to preserve. Fix at Task 8 when the ledger lands.
2. **Ledger-posting module introduced at Task 10, not deferred (Tasks 8, 10, 11).** Task 8's "no shared record-ledger-entry helper — YAGNI until a second caller" expires the moment Task 10 starts: deposit credit (T8), transfer debit (T10), and payout debit (T11) each repeat post-`Transaction` + adjust `FundAccount` balance + `BalanceSnapshot`. That triplet is the money-mutation critical path (PRD §14) and must have one implementation. Task 10 Step 1 becomes: extract the posting module from `ProcessDepositCommandHandler`, then consume it from both new handlers. Interface stays one method (post a ledger entry against a fund account); repositories become its internals.
3. **`TenantScopeResolver` returns `TenantScope`, not `string?` (Tasks 2, 4, 7–12).** Decided in spec `004-subaccount-endpoints-rework` (2026-07-17): closed hierarchy `Single(string ClientCompanyId) | AllTenants` replaces null-means-all-tenants. Single-tenant handlers take a plain `ClientCompanyId` extracted at the endpoint; only all-tenant-capable endpoints (list, Task 12 admin views) match on `TenantScope`. Every `Resolve(...)!` null-forgiving usage in snippets below is superseded — the `!` was the defect's symptom.
4. **`ITransactionRepository.ListAllAsync` takes a filter record (Task 12).** Eight positional parameters (five nullable, two adjacent `DateTime?`) is an unusable interface. Signature becomes `ListAllAsync(TransactionListFilter filter, CancellationToken ct)` with `TransactionListFilter(string? ClientCompanyId, TransactionType? Type, TransactionStatus? Status, DateTime? FromUtc, DateTime? ToUtc, int Page, int PageSize)`.
5. **`SupportedChainsOptions` wraps, never inherits, `List<string>` (Task 7).** Inheriting `List` leaks the whole mutable list surface as interface. Shape: options class holding the configured list, exposing `bool IsSupported(string chain)` (case-insensitive) — validators consume that one method.
6. **Gateway DTO renamed `GeneratedDepositAddress` (Task 7).** Two types named `GenerateDepositAddressResult` in sibling namespaces is pure interface tax; the Ports-namespace gateway DTO takes the new name, the Application command result keeps the old one.
7. **Doc drift vs shipped code (all tasks).** Code as committed uses `ISubAccountGateway` (not Task 0's `ICircleSubAccountGateway`), module-first layout `Application/Compliance/...` (not `Application/SubAccounts/`), `Application.Shared.Ports` (not `Application.Ports.GatewayDtos`), and no `ITenantContext`. Where a snippet's path or type name conflicts with the committed tree, the tree and the B0.5 module boundaries win.
8. **Mocking library is Moq, not NSubstitute (all test snippets, doc-grilling 2026-07-17).** Every test code sample below written against `Substitute.For<T>()` / `sub.Method(...).Returns(...)` / `received.Method(...)` uses NSubstitute syntax. `Directory.Packages.props` and `tests/TreasuryServiceOrchestrator.UnitTests.csproj` reference **Moq** (matches `CLAUDE.md`'s Application-tier testing row: "Moq (mock ports)") — NSubstitute is not a dependency anywhere in the solution. Translate before implementing: `Substitute.For<T>()` → `new Mock<T>()` (use `.Object` for the instance), `sub.Method(x).Returns(y)` → `mock.Setup(s => s.Method(x)).ReturnsAsync(y)` (or `.Returns(y)` for sync), `sub.Received().Method(x)` → `mock.Verify(s => s.Method(x), Times.Once)`. None of these snippets compile as literally pasted.
9. **Caller-identity registry check is `ISubAccountRepository`-backed, not a static allow-list (Task 1, security fix 2026-07-17).** Task 1 below designs `KnownClientCompaniesRegistry`/`KnownClientCompaniesOptions` — a separately-configured list of known caller ids. What's actually shipped in `CallerIdentityMiddleware` closes the PRD §2.2 "registry of known callers" requirement by querying `ISubAccountRepository.GetByClientCompanyIdAsync` directly against persisted `SubAccount` rows: any non-admin `ClientCompanyId` header with no matching `SubAccount` row gets a 401. This is a deliberate supersession, not just doc drift — a `SubAccount` row is the actual source of truth for "is this a known tenant," and a second, separately-maintained config list would just be a second place that can drift from it. Task 1's registry/role-enum shape below is superseded by this; its `ICallerContext`/`CallerRole` design still matches what shipped.

---

## Task 1: Caller registry with roles (Admin | SubAccount)

Replaces the flat `KnownClientCompaniesOptions : List<string>` / `KnownClientCompaniesRegistry.IsKnown` check with a structured caller registry that also yields a role, and introduces `ICallerContext` so downstream code can tell an admin credential from a tenant credential without re-parsing the header.

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Api/Middleware/KnownClientCompaniesRegistry.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Middleware/ClientCompanyIdMiddleware.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/ICallerContext.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Middleware/HttpCallerContext.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.json`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Api/KnownClientCompaniesRegistryTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/ClientCompanyIdMiddlewareTests.cs` (modify existing)
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/CallerContextTests.cs`

**Interfaces:**
- Produces: `CallerRole { SubAccount = 0, Admin = 1 }` enum; `KnownCaller { string Id; CallerRole Role; }` record; `KnownClientCompaniesRegistry.TryResolve(string id, out KnownCaller caller): bool`; `ICallerContext { string CallerId { get; } CallerRole Role { get; } bool IsAdmin => Role == CallerRole.Admin; }`.
- Consumes (later tasks): Task 2's `TenantScopeResolver` consumes `ICallerContext` directly; every controller written from Task 4 onward resolves tenant scope through Task 2's resolver rather than reading headers.

- [ ] **Step 1: Write the failing unit test for the registry**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Api/KnownClientCompaniesRegistryTests.cs
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Api.Middleware;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Api;

public class KnownClientCompaniesRegistryTests
{
    private static KnownClientCompaniesRegistry CreateRegistry(params KnownCaller[] callers)
    {
        var options = new KnownClientCompaniesOptions();
        options.AddRange(callers);
        return new KnownClientCompaniesRegistry(new StaticOptionsMonitor(options));
    }

    [Fact]
    public void TryResolve_returns_true_and_role_for_known_subaccount_caller()
    {
        var registry = CreateRegistry(new KnownCaller("acme-co", CallerRole.SubAccount));

        var resolved = registry.TryResolve("acme-co", out var caller);

        Assert.True(resolved);
        Assert.Equal(CallerRole.SubAccount, caller.Role);
    }

    [Fact]
    public void TryResolve_is_case_insensitive_on_id()
    {
        var registry = CreateRegistry(new KnownCaller("acme-co", CallerRole.SubAccount));

        var resolved = registry.TryResolve("ACME-CO", out var caller);

        Assert.True(resolved);
        Assert.Equal("acme-co", caller.Id);
    }

    [Fact]
    public void TryResolve_returns_false_for_unknown_caller()
    {
        var registry = CreateRegistry(new KnownCaller("acme-co", CallerRole.SubAccount));

        var resolved = registry.TryResolve("unknown-co", out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_returns_admin_role_for_admin_caller()
    {
        var registry = CreateRegistry(new KnownCaller("apiso-admin", CallerRole.Admin));

        registry.TryResolve("apiso-admin", out var caller);

        Assert.Equal(CallerRole.Admin, caller.Role);
    }

    private sealed class StaticOptionsMonitor(KnownClientCompaniesOptions value) : IOptionsMonitor<KnownClientCompaniesOptions>
    {
        public KnownClientCompaniesOptions CurrentValue { get; } = value;
        public KnownClientCompaniesOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<KnownClientCompaniesOptions, string> listener) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*KnownClientCompaniesRegistryTests*"`
Expected: FAIL — compile error, `KnownCaller`/`CallerRole`/`TryResolve` do not exist yet.

- [ ] **Step 3: Rewrite the registry and options**

```csharp
// src/TreasuryServiceOrchestrator.Api/Middleware/KnownClientCompaniesRegistry.cs
using Microsoft.Extensions.Options;

namespace TreasuryServiceOrchestrator.Api.Middleware;

public enum CallerRole
{
    SubAccount = 0,
    Admin = 1,
}

public sealed record KnownCaller(string Id, CallerRole Role);

public sealed class KnownClientCompaniesOptions : List<KnownCaller>;

public sealed class KnownClientCompaniesRegistry(IOptionsMonitor<KnownClientCompaniesOptions> options)
{
    public bool TryResolve(string clientCompanyId, out KnownCaller caller)
    {
        var match = options.CurrentValue
            .FirstOrDefault(c => string.Equals(c.Id, clientCompanyId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            caller = default!;
            return false;
        }

        caller = match;
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*KnownClientCompaniesRegistryTests*"`
Expected: PASS (4 tests)

- [ ] **Step 5: Add `ICallerContext` port**

```csharp
// src/TreasuryServiceOrchestrator.Application/Shared/Ports/ICallerContext.cs
namespace TreasuryServiceOrchestrator.Application.Shared.Ports;

public enum CallerRole
{
    SubAccount = 0,
    Admin = 1,
}

public interface ICallerContext
{
    string CallerId { get; }
    CallerRole Role { get; }
    bool IsAdmin => Role == CallerRole.Admin;
}
```

Note: `Application` cannot reference `Api`, so `CallerRole` is declared independently in `Application.Shared.Ports` (mirroring the existing `Api.Middleware.CallerRole`); `HttpCallerContext` maps one to the other.

- [ ] **Step 6: Rewrite the middleware to resolve and store the caller**

```csharp
// src/TreasuryServiceOrchestrator.Api/Middleware/ClientCompanyIdMiddleware.cs
namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class ClientCompanyIdMiddleware(RequestDelegate next)
{
    private static readonly string[] BypassPaths = ["/health/live", "/health/ready", "/api/v1/webhooks/circle"];

    public async Task InvokeAsync(HttpContext context, KnownClientCompaniesRegistry registry)
    {
        if (BypassPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("ClientCompanyId", out var clientCompanyId)
            || string.IsNullOrWhiteSpace(clientCompanyId)
            || !registry.TryResolve(clientCompanyId!, out var caller))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unknown or missing ClientCompanyId" });
            return;
        }

        context.Items["CallerId"] = caller.Id;
        context.Items["CallerRole"] = caller.Role;
        await next(context);
    }
}
```

- [ ] **Step 7: Add `HttpCallerContext`**

```csharp
// src/TreasuryServiceOrchestrator.Api/Middleware/HttpCallerContext.cs
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class HttpCallerContext(IHttpContextAccessor accessor) : ICallerContext
{
    public string CallerId =>
        accessor.HttpContext?.Items["CallerId"] as string
        ?? throw new InvalidOperationException("No caller resolved for this request.");

    public CallerRole Role =>
        accessor.HttpContext?.Items["CallerRole"] is CallerRole role
            ? (CallerRole)(int)role
            : throw new InvalidOperationException("No caller resolved for this request.");
}
```

- [ ] **Step 8: Wire DI and config in `Program.cs`**

Replace the existing block:
```csharp
builder.Services.Configure<KnownClientCompaniesOptions>(builder.Configuration.GetSection("KnownClientCompanies"));
builder.Services.AddSingleton<KnownClientCompaniesRegistry>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
```
with:
```csharp
builder.Services.Configure<KnownClientCompaniesOptions>(builder.Configuration.GetSection("KnownClientCompanies"));
builder.Services.AddSingleton<KnownClientCompaniesRegistry>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICallerContext, HttpCallerContext>();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
```

Update `appsettings.json` `KnownClientCompanies` from a flat string array to objects:
```json
"KnownClientCompanies": [
  { "Id": "acme-co", "Role": "SubAccount" },
  { "Id": "apiso-admin", "Role": "Admin" }
]
```

- [ ] **Step 9: Update existing integration tests' config shape**

In every `WithWebHostBuilder` config override that sets `KnownClientCompanies:0` as a string (grep `KnownClientCompanies:0` under `tests/`), replace with the object form, e.g.:
```csharp
["KnownClientCompanies:0:Id"] = "test-tenant",
["KnownClientCompanies:0:Role"] = "SubAccount",
```
Files to check: `ClientCompanyIdMiddlewareTests.cs`, `SubAccountsCrossTenantCreationTests.cs`, `SubAccountsDuplicateClientConflictTests.cs`, `SubAccountsIdempotencyReplayTests.cs`, `CrossTenantRedeemIsolationTests.cs`, `TenantIsolationQueryFilterTests.cs` (any that configure this key).

- [ ] **Step 10: Add integration test proving admin vs sub-account resolution**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/CallerContextTests.cs
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class CallerContextTests(SqlServerTestDatabaseFixture fixture) : IClassFixture<SqlServerTestDatabaseFixture>
{
    [Fact]
    public async Task Admin_caller_is_accepted_by_the_middleware()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "apiso-admin";
            config["KnownClientCompanies:0:Role"] = "Admin";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "apiso-admin");

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Unknown_caller_still_gets_401()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "SubAccount";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "nobody");

        var response = await client.PostAsJsonAsync("/api/v1/redeem", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

If `TreasuryServiceOrchestratorWebApplicationFactory` doesn't yet take a config-mutation delegate, check `tests/TreasuryServiceOrchestrator.IntegrationTests/Support/` for the existing factory helper and match its actual constructor shape used by sibling tests (e.g. `SubAccountsCrossTenantCreationTests`) instead of introducing a new one.

- [ ] **Step 11: Run full test suite**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat: structured caller registry with Admin/SubAccount roles"
```

---

## Task 2: Tenant scope resolution + RFC 7807 error contract

Gives every handler a single way to resolve "which tenant does this request actually operate on" (PRD §2.4), and replaces ad-hoc controller try/catch with a global `IExceptionHandler` mapping domain exceptions to `ProblemDetails`.

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Application/Shared/TenantScopeResolver.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Exceptions/TreasuryDomainException.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Exceptions/TenantForbiddenException.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Exceptions/NotFoundException.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Exceptions/ConflictException.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Exceptions/ProviderRejectedException.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Exceptions/ProviderUnavailableException.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/ErrorHandling/TreasuryProblemDetailsExceptionHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Controllers/RedeemController.cs` (drop try/catch)
- Modify: `src/TreasuryServiceOrchestrator.Api/Controllers/SubAccountsController.cs` (drop try/catch)
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/TenantScopeResolverTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/ProblemDetailsMappingTests.cs`

**Interfaces:**
- Consumes: Task 1's `ICallerContext { CallerId, Role, IsAdmin }`.
- Produces: `static class TenantScopeResolver { static string? Resolve(ICallerContext caller, string? requestedClientCompanyId); }` — every later handler/controller that needs "the tenant for this request" calls this instead of reading `ITenantContext` directly for anything admin-reachable. `TenantForbiddenException(string message)`, `NotFoundException(string message)`, `ConflictException(string message)`, `ProviderRejectedException(string message)`, `ProviderUnavailableException(string message)` — all `sealed`, all deriving from `abstract class TreasuryDomainException(string message) : Exception(message)`.

- [ ] **Step 1: Write the failing resolver unit tests**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/TenantScopeResolverTests.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application;

public class TenantScopeResolverTests
{
    private sealed class FakeCallerContext(string callerId, CallerRole role) : ICallerContext
    {
        public string CallerId { get; } = callerId;
        public CallerRole Role { get; } = role;
    }

    [Fact]
    public void SubAccount_caller_with_no_requested_scope_resolves_to_own_id()
    {
        var caller = new FakeCallerContext("acme-co", CallerRole.SubAccount);

        var resolved = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: null);

        Assert.Equal("acme-co", resolved);
    }

    [Fact]
    public void SubAccount_caller_requesting_own_id_resolves_to_own_id()
    {
        var caller = new FakeCallerContext("acme-co", CallerRole.SubAccount);

        var resolved = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: "acme-co");

        Assert.Equal("acme-co", resolved);
    }

    [Fact]
    public void SubAccount_caller_requesting_another_id_throws_TenantForbidden()
    {
        var caller = new FakeCallerContext("acme-co", CallerRole.SubAccount);

        Assert.Throws<TenantForbiddenException>(() =>
            TenantScopeResolver.Resolve(caller, requestedClientCompanyId: "other-co"));
    }

    [Fact]
    public void Admin_caller_requesting_a_named_tenant_resolves_to_that_tenant()
    {
        var caller = new FakeCallerContext("apiso-admin", CallerRole.Admin);

        var resolved = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: "acme-co");

        Assert.Equal("acme-co", resolved);
    }

    [Fact]
    public void Admin_caller_with_no_requested_scope_resolves_to_null_meaning_all_tenants()
    {
        var caller = new FakeCallerContext("apiso-admin", CallerRole.Admin);

        var resolved = TenantScopeResolver.Resolve(caller, requestedClientCompanyId: null);

        Assert.Null(resolved);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*TenantScopeResolverTests*"`
Expected: FAIL — compile error, types don't exist yet.

- [ ] **Step 3: Add the exception hierarchy**

```csharp
// src/TreasuryServiceOrchestrator.Application/Exceptions/TreasuryDomainException.cs
namespace TreasuryServiceOrchestrator.Application.Exceptions;

public abstract class TreasuryDomainException(string message) : Exception(message);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Exceptions/TenantForbiddenException.cs
namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class TenantForbiddenException(string message) : TreasuryDomainException(message);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Exceptions/NotFoundException.cs
namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class NotFoundException(string message) : TreasuryDomainException(message);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Exceptions/ConflictException.cs
namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class ConflictException(string message) : TreasuryDomainException(message);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Exceptions/ProviderRejectedException.cs
namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class ProviderRejectedException(string message) : TreasuryDomainException(message);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Exceptions/ProviderUnavailableException.cs
namespace TreasuryServiceOrchestrator.Application.Exceptions;

public sealed class ProviderUnavailableException(string message) : TreasuryDomainException(message);
```

- [ ] **Step 4: Add `TenantScopeResolver`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Shared/TenantScopeResolver.cs
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Application.Shared;

public static class TenantScopeResolver
{
    public static string? Resolve(ICallerContext caller, string? requestedClientCompanyId)
    {
        if (!caller.IsAdmin)
        {
            if (requestedClientCompanyId is null || requestedClientCompanyId == caller.CallerId)
            {
                return caller.CallerId;
            }

            throw new TenantForbiddenException(
                $"Caller '{caller.CallerId}' may not act on behalf of tenant '{requestedClientCompanyId}'.");
        }

        return requestedClientCompanyId;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*TenantScopeResolverTests*"`
Expected: PASS (5 tests)

- [ ] **Step 6: Add the global exception handler**

```csharp
// src/TreasuryServiceOrchestrator.Api/ErrorHandling/TreasuryProblemDetailsExceptionHandler.cs
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Api.ErrorHandling;

public sealed class TreasuryProblemDetailsExceptionHandler(ILogger<TreasuryProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, type) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "validation"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", "not-found"),
            TenantForbiddenException => (StatusCodes.Status403Forbidden, "Tenant forbidden", "tenant-forbidden"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", "conflict"),
            ProviderRejectedException => (StatusCodes.Status422UnprocessableEntity, "Provider rejected request", "provider-rejected"),
            ProviderUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Provider unavailable", "provider-unavailable"),
            _ => (0, "", ""),
        };

        if (status == 0)
        {
            logger.LogError(exception, "Unhandled exception");
            return false;
        }

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Type = $"urn:treasury-service-orchestrator:error:{type}",
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, ct);
        return true;
    }
}
```

- [ ] **Step 7: Wire it in `Program.cs`**

Add near the other `builder.Services.Add...` calls:
```csharp
builder.Services.AddExceptionHandler<TreasuryProblemDetailsExceptionHandler>();
builder.Services.AddProblemDetails();
```
Add immediately after `var app = builder.Build();`, before `app.UseSerilogRequestLogging();`:
```csharp
app.UseExceptionHandler();
```

- [ ] **Step 8: Drop try/catch from existing controllers**

Read `src/TreasuryServiceOrchestrator.Api/Controllers/RedeemController.cs` and `SubAccountsController.cs` in full. Remove any `try { ... } catch (SomeException ex) { return StatusCode(...); }` wrapping around handler calls — let the exception propagate to the new global handler. Replace domain-specific manual status codes (e.g. a hand-rolled 409 on `InvalidOperationException`) by throwing the matching new exception type (`ConflictException`, `NotFoundException`, etc.) from the handler/repository layer instead of the controller catching a generic one. If a controller currently catches a generic `Exception` to return a specific status, that logic must move into the handler as a typed exception — controllers only dispatch.

- [ ] **Step 9: Add integration test proving the mapping**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/ProblemDetailsMappingTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class ProblemDetailsMappingTests(SqlServerTestDatabaseFixture fixture) : IClassFixture<SqlServerTestDatabaseFixture>
{
    [Fact]
    public async Task Redeem_with_missing_required_fields_returns_validation_problem_details()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "SubAccount";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");

        var response = await client.PostAsJsonAsync("/api/v1/redeem", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestContext.Current.CancellationToken);
        Assert.Equal("urn:treasury-service-orchestrator:error:validation", body!["type"].ToString());
    }
}
```

- [ ] **Step 10: Run full suite, fix any test expecting old error shapes**

Run: `dotnet build`
Expected: 0 warnings.

Run: `dotnet test`
Expected: all green — grep test source for `StatusCode.*409|StatusCode.*404` assertions tied to the controllers touched in Step 8 and update expected bodies to the new ProblemDetails `type` values if they assert response body shape.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: tenant scope resolver and RFC 7807 exception handling"
```

---

## Task 3: Sub-account lifecycle + `EntityRegistration` domain rework

Splits business/address data off `SubAccount` into a new `EntityRegistration` entity that carries the Institutional-API registration lifecycle (PRD §3.1/§3.2), and gives `SubAccount` its own overlay lifecycle (`Created → PendingCompliance → Active|Rejected`) plus an independent `IsDisabled` flag (admin can disable/enable regardless of compliance state). This decouples "is Circle happy with this business" from "is this sub-account usable right now."

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Domain/SubAccount.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/SubAccountLifecycleState.cs`
- Rename+Modify: `src/TreasuryServiceOrchestrator.Domain/SubAccountComplianceState.cs` → `src/TreasuryServiceOrchestrator.Domain/EntityRegistrationStatus.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/EntityRegistration.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/CreateSubAccountCommandHandler.cs`
- Rename+Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/SubAccountComplianceStateMapper.cs` → `EntityRegistrationStatusMapper.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/ProcessExternalEntityDecisionHandler.cs` (rename decision target to `EntityRegistration`, cascade to `SubAccount.LifecycleState`)
- Create: `src/TreasuryServiceOrchestrator.Application/Compliance/Ports/IEntityRegistrationRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/EntityRegistrationRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Delete + regenerate: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*` (`InitialCreate`)
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/CreateSubAccountCommandHandlerTests.cs` (modify existing assertions)
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/ProcessExternalEntityDecisionHandlerTests.cs` (modify existing assertions)

**Interfaces:**
- Consumes: existing `ISubAccountGateway.CreateExternalEntityAsync` (unchanged signature — still returns `CreateExternalEntityResult(string WalletId, string ComplianceState, string BusinessName, string BusinessUniqueIdentifier)`; the handler now treats `WalletId` as an immediate wallet assignment and `ComplianceState` as the *initial* registration status, not a final decision).
- Produces: `enum SubAccountLifecycleState { Created, PendingCompliance, Active, Rejected }`; `SubAccount { ..., SubAccountLifecycleState LifecycleState, bool IsDisabled }` (no more `BusinessName`/`BusinessUniqueIdentifier`); `enum EntityRegistrationStatus { Pending, Accepted, Rejected }`; `class EntityRegistration { Guid Id; Guid SubAccountId; string ClientCompanyId; string BusinessName; string BusinessUniqueIdentifier; string IdentifierIssuingCountryCode; string Country; string State; string City; string Postcode; string StreetName; string BuildingNumber; string? CircleWalletId; EntityRegistrationStatus Status; string? RejectionReason; DateTime CreatedAtUtc; DateTime UpdatedAtUtc; }`; `IEntityRegistrationRepository { Task AddAsync(EntityRegistration, ct); Task<EntityRegistration?> GetByCircleWalletIdAsync(string walletId, ct); Task<EntityRegistration?> GetLatestForSubAccountAsync(Guid subAccountId, ct); }`. Task 4's resubmit endpoint and Task 5's `externalEntities` webhook processor both operate on `EntityRegistration`, then cascade `SubAccount.LifecycleState`.

- [ ] **Step 1: Update the failing handler tests to the new shape**

Read `tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/CreateSubAccountCommandHandlerTests.cs` and `ProcessExternalEntityDecisionHandlerTests.cs` in full. Update every assertion referencing `subAccount.BusinessName`, `subAccount.ComplianceState`, or a mocked `ISubAccountRepository` interaction that assumed the old shape, to instead assert:
- `CreateSubAccountCommandHandlerTests`: on success, `subAccounts.AddAsync` is called with a `SubAccount` whose `LifecycleState == SubAccountLifecycleState.PendingCompliance` and `CircleWalletId` set from the gateway result, **and** a mocked `IEntityRegistrationRepository.AddAsync` is called with an `EntityRegistration` carrying the command's business/address fields and `Status` mapped from the gateway's `ComplianceState`.
- `ProcessExternalEntityDecisionHandlerTests`: the handler now looks up `EntityRegistration` by `CircleWalletId` (via `IEntityRegistrationRepository`), updates its `Status`, and — only when the new status is `Accepted` or `Rejected` — cascades `SubAccount.LifecycleState` to `Active` or `Rejected` respectively (looked up via `ISubAccountRepository.GetByCircleWalletIdAsync`, unchanged signature).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*CreateSubAccountCommandHandlerTests*|*ProcessExternalEntityDecisionHandlerTests*"`
Expected: FAIL — compile errors, new types/members don't exist yet.

- [ ] **Step 3: Add domain types**

```csharp
// src/TreasuryServiceOrchestrator.Domain/SubAccountLifecycleState.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum SubAccountLifecycleState
{
    Created,
    PendingCompliance,
    Active,
    Rejected,
}
```

Delete `SubAccountComplianceState.cs` and create:
```csharp
// src/TreasuryServiceOrchestrator.Domain/EntityRegistrationStatus.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum EntityRegistrationStatus
{
    Pending,
    Accepted,
    Rejected,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/EntityRegistration.cs
namespace TreasuryServiceOrchestrator.Domain;

public class EntityRegistration
{
    public Guid Id { get; set; }
    public required Guid SubAccountId { get; set; }
    public required string ClientCompanyId { get; set; }
    public required string BusinessName { get; set; }
    public required string BusinessUniqueIdentifier { get; set; }
    public required string IdentifierIssuingCountryCode { get; set; }
    public required string Country { get; set; }
    public required string State { get; set; }
    public required string City { get; set; }
    public required string Postcode { get; set; }
    public required string StreetName { get; set; }
    public required string BuildingNumber { get; set; }
    public string? CircleWalletId { get; set; }
    public EntityRegistrationStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/SubAccount.cs
namespace TreasuryServiceOrchestrator.Domain;

public class SubAccount
{
    public Guid Id { get; set; }
    public required string ClientCompanyId { get; set; }
    public string? CircleWalletId { get; set; }
    public SubAccountLifecycleState LifecycleState { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

- [ ] **Step 4: Add `IEntityRegistrationRepository` and its EF implementation**

```csharp
// src/TreasuryServiceOrchestrator.Application/Compliance/Ports/IEntityRegistrationRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.Ports;

public interface IEntityRegistrationRepository
{
    Task AddAsync(EntityRegistration registration, CancellationToken cancellationToken = default);
    Task<EntityRegistration?> GetByCircleWalletIdAsync(string walletId, CancellationToken cancellationToken = default);
    Task<EntityRegistration?> GetLatestForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken = default);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/EntityRegistrationRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class EntityRegistrationRepository(TreasuryServiceOrchestratorDbContext dbContext)
    : IEntityRegistrationRepository
{
    public async Task AddAsync(EntityRegistration registration, CancellationToken cancellationToken = default)
        => await dbContext.EntityRegistrations.AddAsync(registration, cancellationToken);

    public Task<EntityRegistration?> GetByCircleWalletIdAsync(string walletId, CancellationToken cancellationToken = default)
        => dbContext.EntityRegistrations.SingleOrDefaultAsync(r => r.CircleWalletId == walletId, cancellationToken);

    public Task<EntityRegistration?> GetLatestForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken = default)
        => dbContext.EntityRegistrations
            .Where(r => r.SubAccountId == subAccountId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
```

- [ ] **Step 5: Rework `CreateSubAccountCommandHandler`**

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/CreateSubAccountCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;
using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed class CreateSubAccountCommandHandler(
    ISubAccountGateway gateway,
    IIdempotencyService idempotency,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations,
    IValidator<CreateSubAccountCommand> validator)
    : ICommandHandler<CreateSubAccountCommand, CreateSubAccountResult>
{
    public async Task<CreateSubAccountResult> HandleAsync(
        CreateSubAccountCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            command.ClientCompanyId,
            command.IdempotencyKey,
            new
            {
                command.BusinessName,
                command.BusinessUniqueIdentifier,
                command.IdentifierIssuingCountryCode,
                command.Country,
                command.State,
                command.City,
                command.Postcode,
                command.StreetName,
                command.BuildingNumber,
            },
            unitOfWork,
            async () =>
            {
                if (await subAccounts.GetByClientCompanyIdAsync(command.ClientCompanyId, cancellationToken) is not null)
                    throw new SubAccountAlreadyExistsException(command.ClientCompanyId);

                await auditLog.AppendAsync(
                    "SubAccountRequested", "SubAccount", command.CorrelationId,
                    JsonSerializer.Serialize(command), command.ClientCompanyId, command.CorrelationId, cancellationToken);

                var gatewayResult = await gateway.CreateExternalEntityAsync(
                    new CreateExternalEntityGatewayRequest(
                        BusinessName: command.BusinessName,
                        BusinessUniqueIdentifier: command.BusinessUniqueIdentifier,
                        IdentifierIssuingCountryCode: command.IdentifierIssuingCountryCode,
                        Address: new ExternalEntityAddress(
                            Country: command.Country,
                            State: command.State,
                            City: command.City,
                            Postcode: command.Postcode,
                            StreetName: command.StreetName,
                            BuildingNumber: command.BuildingNumber)),
                    cancellationToken);

                var registrationStatus = EntityRegistrationStatusMapper.Map(gatewayResult.ComplianceState);

                var subAccount = new SubAccount
                {
                    Id = Guid.NewGuid(),
                    ClientCompanyId = command.ClientCompanyId,
                    CircleWalletId = gatewayResult.WalletId,
                    LifecycleState = SubAccountLifecycleState.PendingCompliance,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                await subAccounts.AddAsync(subAccount, cancellationToken);

                var registration = new EntityRegistration
                {
                    Id = Guid.NewGuid(),
                    SubAccountId = subAccount.Id,
                    ClientCompanyId = command.ClientCompanyId,
                    BusinessName = command.BusinessName,
                    BusinessUniqueIdentifier = command.BusinessUniqueIdentifier,
                    IdentifierIssuingCountryCode = command.IdentifierIssuingCountryCode,
                    Country = command.Country,
                    State = command.State,
                    City = command.City,
                    Postcode = command.Postcode,
                    StreetName = command.StreetName,
                    BuildingNumber = command.BuildingNumber,
                    CircleWalletId = gatewayResult.WalletId,
                    Status = registrationStatus,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                await entityRegistrations.AddAsync(registration, cancellationToken);

                if (registrationStatus != EntityRegistrationStatus.Pending)
                {
                    subAccount.LifecycleState = registrationStatus == EntityRegistrationStatus.Accepted
                        ? SubAccountLifecycleState.Active
                        : SubAccountLifecycleState.Rejected;
                }

                await auditLog.AppendAsync(
                    "SubAccountProvisionedAtCircle", "SubAccount", subAccount.Id.ToString(),
                    JsonSerializer.Serialize(new { subAccount.CircleWalletId, registrationStatus }),
                    command.ClientCompanyId, command.CorrelationId, cancellationToken);

                return new CreateSubAccountResult(subAccount.Id, subAccount.CircleWalletId!, subAccount.LifecycleState);
            },
            cancellationToken);
    }
}
```

Update `CreateSubAccountResult` (`src/TreasuryServiceOrchestrator.Application/SubAccounts/CreateSubAccountResult.cs`) to carry `SubAccountLifecycleState` instead of `SubAccountComplianceState` in its record signature.

- [ ] **Step 6: Rename the mapper and rework `ProcessExternalEntityDecisionHandler`**

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/EntityRegistrationStatusMapper.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

internal static class EntityRegistrationStatusMapper
{
    public static EntityRegistrationStatus Map(string circleComplianceState)
        => Enum.TryParse<EntityRegistrationStatus>(circleComplianceState, ignoreCase: true, out var state)
            ? state
            : throw new InvalidOperationException($"Unknown Circle complianceState '{circleComplianceState}'.");
}
```

Delete `SubAccountComplianceStateMapper.cs`.

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/ProcessExternalEntityDecisionHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed class ProcessExternalEntityDecisionHandler(
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ProcessExternalEntityDecisionCommand, ProcessExternalEntityDecisionResult>
{
    public async Task<ProcessExternalEntityDecisionResult> HandleAsync(
        ProcessExternalEntityDecisionCommand command, CancellationToken cancellationToken = default)
    {
        var registration = await entityRegistrations.GetByCircleWalletIdAsync(command.WalletId, cancellationToken)
            ?? throw new SubAccountNotFoundException(command.WalletId);

        var newStatus = EntityRegistrationStatusMapper.Map(command.ComplianceState);

        var subAccount = await subAccounts.GetByCircleWalletIdAsync(command.WalletId, cancellationToken)
            ?? throw new SubAccountNotFoundException(command.WalletId);

        if (registration.Status == newStatus)
            return new ProcessExternalEntityDecisionResult(subAccount.Id, subAccount.LifecycleState);

        var previousStatus = registration.Status;
        registration.Status = newStatus;
        registration.UpdatedAtUtc = DateTime.UtcNow;

        if (newStatus is EntityRegistrationStatus.Accepted or EntityRegistrationStatus.Rejected)
        {
            subAccount.LifecycleState = newStatus == EntityRegistrationStatus.Accepted
                ? SubAccountLifecycleState.Active
                : SubAccountLifecycleState.Rejected;
            subAccount.UpdatedAtUtc = DateTime.UtcNow;
        }

        await auditLog.AppendAsync(
            "SubAccountComplianceDecision", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(new { command.WalletId, PreviousStatus = previousStatus, NewStatus = newStatus }),
            subAccount.ClientCompanyId, command.WalletId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessExternalEntityDecisionResult(subAccount.Id, subAccount.LifecycleState);
    }
}
```

Update `ProcessExternalEntityDecisionResult` to carry `SubAccountLifecycleState` instead of `SubAccountComplianceState`.

- [ ] **Step 7: Add `EntityRegistration` DbContext mapping**

In `TreasuryServiceOrchestratorDbContext.cs`, add `public DbSet<EntityRegistration> EntityRegistrations => Set<EntityRegistration>();` next to `SubAccounts`, and in `OnModelCreating` add:
```csharp
modelBuilder.Entity<EntityRegistration>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
    entity.HasIndex(e => e.SubAccountId);
    entity.HasIndex(e => e.CircleWalletId);
});
```
The existing `SubAccount` block's `entity.HasIndex(e => e.CircleWalletId).IsUnique();` stays — `SubAccount.CircleWalletId` is still one-per-sub-account; `EntityRegistration.CircleWalletId` is non-unique (a resubmit creates a new registration row against the same wallet in later tasks).

- [ ] **Step 8: Regenerate `InitialCreate`**

```bash
rm src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*InitialCreate*.cs src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/TreasuryServiceOrchestratorDbContextModelSnapshot.cs
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
```
Expected: new migration creates `EntityRegistrations` table, `SubAccounts` table drops `BusinessName`/`BusinessUniqueIdentifier`/`ComplianceState` columns and gains `LifecycleState`/`IsDisabled`.

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet build`
Expected: 0 warnings.

Run: `dotnet test`
Expected: all green. Any other test seeding a raw `SubAccount` (grep `new SubAccount` under `tests/`) must drop the removed properties and add `LifecycleState`/`IsDisabled`.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: split EntityRegistration from SubAccount, add lifecycle state + disabled overlay"
```

---

## Task 4: Sub-account endpoints rework (create/get/list/disable/enable/resubmit)

Reworks `SubAccountsController` per PRD §4.1: **create is Admin-only** (targets an explicit `ClientCompanyId`, never the caller's own — an Admin credential has no sub-account of its own), get/list/disable/enable/resubmit all route through Task 2's `TenantScopeResolver` so a SubAccount caller only ever sees its own tenant and an Admin must name the target explicitly.

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Api/Controllers/SubAccountsController.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/CreateSubAccountCommand.cs` (add `TargetClientCompanyId`, keep `ClientCompanyId` as caller identity for audit)
- Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/CreateSubAccountCommandHandler.cs` (use `TargetClientCompanyId` as the tenant being created)
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountResult.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/ListSubAccountsQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/ListSubAccountsQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/SetSubAccountDisabledCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/SetSubAccountDisabledCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/ResubmitEntityRegistrationCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/SubAccounts/ResubmitEntityRegistrationCommandHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Compliance/Ports/ISubAccountRepository.cs` (add `ListAsync`)
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/SubAccountRepository.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/GetSubAccountQueryHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/ResubmitEntityRegistrationCommandHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/SubAccountsEndpointsTests.cs`

**Interfaces:**
- Consumes: `ICallerContext` (Task 1), `TenantScopeResolver.Resolve(ICallerContext, string? requestedClientCompanyId)` (Task 2), `IEntityRegistrationRepository` (Task 3), exception types `TenantForbiddenException`/`NotFoundException`/`ConflictException` (Task 2).
- Produces: `GetSubAccountQuery(string ResolvedClientCompanyId)`; `GetSubAccountResult(Guid SubAccountId, string ClientCompanyId, SubAccountLifecycleState LifecycleState, bool IsDisabled, string? CircleWalletId, EntityRegistrationStatus? LatestRegistrationStatus, string? RejectionReason)`; `ListSubAccountsQuery(SubAccountLifecycleState? StateFilter)` (Admin-only, resolver returns `null` scope meaning "all tenants"); `SetSubAccountDisabledCommand(string ResolvedClientCompanyId, bool Disabled, string CorrelationId)`; `ResubmitEntityRegistrationCommand(string ResolvedClientCompanyId, string IdempotencyKey, string BusinessName, string BusinessUniqueIdentifier, string IdentifierIssuingCountryCode, string Country, string State, string City, string Postcode, string StreetName, string BuildingNumber, string CorrelationId)`.

- [ ] **Step 1: Write failing unit tests for `GetSubAccountQueryHandler` and `ResubmitEntityRegistrationCommandHandler`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/GetSubAccountQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.SubAccounts;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.SubAccounts;

public class GetSubAccountQueryHandlerTests
{
    [Fact]
    public async Task Returns_latest_registration_status_and_rejection_reason()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Rejected, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var registration = new EntityRegistration
        {
            Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme",
            BusinessName = "Acme Co", BusinessUniqueIdentifier = "123", IdentifierIssuingCountryCode = "US",
            Country = "US", State = "NY", City = "NYC", Postcode = "10001", StreetName = "Main St", BuildingNumber = "1",
            CircleWalletId = "wallet-1", Status = EntityRegistrationStatus.Rejected, RejectionReason = "sanctions match",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };

        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", TestContext.Current.CancellationToken).Returns(subAccount);
        var registrations = Substitute.For<IEntityRegistrationRepository>();
        registrations.GetLatestForSubAccountAsync(subAccount.Id, TestContext.Current.CancellationToken).Returns(registration);

        var handler = new GetSubAccountQueryHandler(subAccounts, registrations);

        var result = await handler.HandleAsync(new GetSubAccountQuery("acme"), TestContext.Current.CancellationToken);

        Assert.Equal(SubAccountLifecycleState.Rejected, result.LifecycleState);
        Assert.Equal(EntityRegistrationStatus.Rejected, result.LatestRegistrationStatus);
        Assert.Equal("sanctions match", result.RejectionReason);
    }

    [Fact]
    public async Task Throws_NotFoundException_when_sub_account_does_not_exist()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("missing", TestContext.Current.CancellationToken).Returns((SubAccount?)null);
        var handler = new GetSubAccountQueryHandler(subAccounts, Substitute.For<IEntityRegistrationRepository>());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetSubAccountQuery("missing"), TestContext.Current.CancellationToken));
    }
}
```

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/ResubmitEntityRegistrationCommandHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.SubAccounts;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.SubAccounts;

public class ResubmitEntityRegistrationCommandHandlerTests
{
    private static ResubmitEntityRegistrationCommand Command(string clientCompanyId = "acme") => new(
        clientCompanyId, "idem-1", "Acme Co", "123", "US", "US", "NY", "NYC", "10001", "Main St", "1", "corr-1");

    [Fact]
    public async Task Throws_ConflictException_when_sub_account_is_not_Rejected()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", TestContext.Current.CancellationToken).Returns(subAccount);

        var handler = new ResubmitEntityRegistrationCommandHandler(
            Substitute.For<ISubAccountGateway>(), Substitute.For<IIdempotencyService>(),
            Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>(),
            subAccounts, Substitute.For<IEntityRegistrationRepository>());

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(Command(), TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*GetSubAccountQueryHandlerTests*|*ResubmitEntityRegistrationCommandHandlerTests*"`
Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Add query/result and repository list support**

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountQuery.cs
namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed record GetSubAccountQuery(string ResolvedClientCompanyId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountResult.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed record GetSubAccountResult(
    Guid SubAccountId,
    string ClientCompanyId,
    SubAccountLifecycleState LifecycleState,
    bool IsDisabled,
    string? CircleWalletId,
    EntityRegistrationStatus? LatestRegistrationStatus,
    string? RejectionReason);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed class GetSubAccountQueryHandler(
    ISubAccountRepository subAccounts, IEntityRegistrationRepository entityRegistrations)
    : IQueryHandler<GetSubAccountQuery, GetSubAccountResult>
{
    public async Task<GetSubAccountResult> HandleAsync(GetSubAccountQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account for client '{query.ResolvedClientCompanyId}'.");

        var registration = await entityRegistrations.GetLatestForSubAccountAsync(subAccount.Id, cancellationToken);

        return new GetSubAccountResult(
            subAccount.Id, subAccount.ClientCompanyId, subAccount.LifecycleState, subAccount.IsDisabled,
            subAccount.CircleWalletId, registration?.Status, registration?.RejectionReason);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/ListSubAccountsQuery.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed record ListSubAccountsQuery(SubAccountLifecycleState? StateFilter);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/ListSubAccountsQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Compliance.Ports;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed class ListSubAccountsQueryHandler(ISubAccountRepository subAccounts)
    : IQueryHandler<ListSubAccountsQuery, IReadOnlyList<GetSubAccountResult>>
{
    public async Task<IReadOnlyList<GetSubAccountResult>> HandleAsync(
        ListSubAccountsQuery query, CancellationToken cancellationToken = default)
    {
        var all = await subAccounts.ListAsync(query.StateFilter, cancellationToken);
        return all.Select(s => new GetSubAccountResult(
            s.Id, s.ClientCompanyId, s.LifecycleState, s.IsDisabled, s.CircleWalletId, null, null)).ToList();
    }
}
```

Add to `ISubAccountRepository`:
```csharp
Task<IReadOnlyList<SubAccount>> ListAsync(SubAccountLifecycleState? stateFilter, CancellationToken cancellationToken);
```
Implement in `SubAccountRepository` with `dbContext.SubAccounts.Where(s => stateFilter == null || s.LifecycleState == stateFilter).ToListAsync(cancellationToken)` (cast to `IReadOnlyList<SubAccount>`).

- [ ] **Step 4: Add disable/enable and resubmit commands**

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/SetSubAccountDisabledCommand.cs
namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed record SetSubAccountDisabledCommand(string ResolvedClientCompanyId, bool Disabled, string CorrelationId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/SetSubAccountDisabledCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed class SetSubAccountDisabledCommandHandler(
    ISubAccountRepository subAccounts, IAuditLogService auditLog, IUnitOfWork unitOfWork)
    : ICommandHandler<SetSubAccountDisabledCommand, GetSubAccountResult>
{
    public async Task<GetSubAccountResult> HandleAsync(
        SetSubAccountDisabledCommand command, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account for client '{command.ResolvedClientCompanyId}'.");

        subAccount.IsDisabled = command.Disabled;
        subAccount.UpdatedAtUtc = DateTime.UtcNow;

        await auditLog.AppendAsync(
            command.Disabled ? "SubAccountDisabled" : "SubAccountEnabled", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(new { command.Disabled }), command.ResolvedClientCompanyId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GetSubAccountResult(subAccount.Id, subAccount.ClientCompanyId, subAccount.LifecycleState,
            subAccount.IsDisabled, subAccount.CircleWalletId, null, null);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/ResubmitEntityRegistrationCommand.cs
namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed record ResubmitEntityRegistrationCommand(
    string ResolvedClientCompanyId, string IdempotencyKey, string BusinessName, string BusinessUniqueIdentifier,
    string IdentifierIssuingCountryCode, string Country, string State, string City, string Postcode,
    string StreetName, string BuildingNumber, string CorrelationId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/ResubmitEntityRegistrationCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed class ResubmitEntityRegistrationCommandHandler(
    ISubAccountGateway gateway, IIdempotencyService idempotency, IAuditLogService auditLog, IUnitOfWork unitOfWork,
    ISubAccountRepository subAccounts, IEntityRegistrationRepository entityRegistrations)
    : ICommandHandler<ResubmitEntityRegistrationCommand, GetSubAccountResult>
{
    public async Task<GetSubAccountResult> HandleAsync(
        ResubmitEntityRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        return await IdempotencyExecutor.ExecuteAsync(
            idempotency, command.ResolvedClientCompanyId, command.IdempotencyKey,
            new { command.BusinessName, command.BusinessUniqueIdentifier }, unitOfWork,
            async () =>
            {
                var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
                    ?? throw new NotFoundException($"No sub-account for client '{command.ResolvedClientCompanyId}'.");

                if (subAccount.LifecycleState != SubAccountLifecycleState.Rejected)
                    throw new ConflictException("Resubmission is only allowed from the Rejected state.");

                var gatewayResult = await gateway.CreateExternalEntityAsync(
                    new CreateExternalEntityGatewayRequest(
                        command.BusinessName, command.BusinessUniqueIdentifier, command.IdentifierIssuingCountryCode,
                        new ExternalEntityAddress(command.Country, command.State, command.City, command.Postcode,
                            command.StreetName, command.BuildingNumber)),
                    cancellationToken);

                var registrationStatus = EntityRegistrationStatusMapper.Map(gatewayResult.ComplianceState);

                var registration = new EntityRegistration
                {
                    Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = command.ResolvedClientCompanyId,
                    BusinessName = command.BusinessName, BusinessUniqueIdentifier = command.BusinessUniqueIdentifier,
                    IdentifierIssuingCountryCode = command.IdentifierIssuingCountryCode,
                    Country = command.Country, State = command.State, City = command.City, Postcode = command.Postcode,
                    StreetName = command.StreetName, BuildingNumber = command.BuildingNumber,
                    CircleWalletId = gatewayResult.WalletId, Status = registrationStatus,
                    CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                };
                await entityRegistrations.AddAsync(registration, cancellationToken);

                subAccount.LifecycleState = registrationStatus == EntityRegistrationStatus.Pending
                    ? SubAccountLifecycleState.PendingCompliance
                    : registrationStatus == EntityRegistrationStatus.Accepted
                        ? SubAccountLifecycleState.Active
                        : SubAccountLifecycleState.Rejected;
                subAccount.UpdatedAtUtc = DateTime.UtcNow;

                await auditLog.AppendAsync("SubAccountResubmitted", "SubAccount", subAccount.Id.ToString(),
                    JsonSerializer.Serialize(new { registration.Id, registrationStatus }),
                    command.ResolvedClientCompanyId, command.CorrelationId, cancellationToken);

                return new GetSubAccountResult(subAccount.Id, subAccount.ClientCompanyId, subAccount.LifecycleState,
                    subAccount.IsDisabled, subAccount.CircleWalletId, registration.Status, registration.RejectionReason);
            },
            cancellationToken);
    }
}
```

- [ ] **Step 5: Rework `SubAccountsController`**

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/SubAccountsController.cs
using Asp.Versioning;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.SubAccounts;
using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts")]
public sealed class SubAccountsController(
    ICommandHandler<CreateSubAccountCommand, CreateSubAccountResult> createHandler,
    IQueryHandler<GetSubAccountQuery, GetSubAccountResult> getHandler,
    IQueryHandler<ListSubAccountsQuery, IReadOnlyList<GetSubAccountResult>> listHandler,
    ICommandHandler<SetSubAccountDisabledCommand, GetSubAccountResult> setDisabledHandler,
    ICommandHandler<ResubmitEntityRegistrationCommand, GetSubAccountResult> resubmitHandler,
    ICallerContext caller) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSubAccountRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
            throw new Application.Exceptions.TenantForbiddenException("Only Admin may create sub-accounts.");

        var command = new CreateSubAccountCommand(
            ClientCompanyId: request.TargetClientCompanyId, IdempotencyKey: idempotencyKey,
            BusinessName: request.BusinessName, BusinessUniqueIdentifier: request.BusinessUniqueIdentifier,
            IdentifierIssuingCountryCode: request.IdentifierIssuingCountryCode, Country: request.Country,
            State: request.State, City: request.City, Postcode: request.Postcode, StreetName: request.StreetName,
            BuildingNumber: request.BuildingNumber, CorrelationId: HttpContext.TraceIdentifier);

        var result = await createHandler.HandleAsync(command, cancellationToken);
        return Accepted(result);
    }

    [HttpGet("{clientCompanyId}")]
    public async Task<IActionResult> Get(string clientCompanyId, CancellationToken cancellationToken)
    {
        var scope = TenantScopeResolver.Resolve(caller, clientCompanyId);
        var result = await getHandler.HandleAsync(new GetSubAccountQuery(scope!), cancellationToken);
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Domain.SubAccountLifecycleState? state, CancellationToken cancellationToken)
    {
        TenantScopeResolver.Resolve(caller, requestedClientCompanyId: null);
        if (!caller.IsAdmin)
            throw new Application.Exceptions.TenantForbiddenException("Only Admin may list sub-accounts.");

        var result = await listHandler.HandleAsync(new ListSubAccountsQuery(state), cancellationToken);
        return Ok(result);
    }

    [HttpPut("{clientCompanyId}/disabled")]
    public async Task<IActionResult> SetDisabled(
        string clientCompanyId, [FromBody] SetSubAccountDisabledRequest request, CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
            throw new Application.Exceptions.TenantForbiddenException("Only Admin may disable/enable sub-accounts.");

        var result = await setDisabledHandler.HandleAsync(
            new SetSubAccountDisabledCommand(clientCompanyId, request.Disabled, HttpContext.TraceIdentifier), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{clientCompanyId}/resubmit")]
    public async Task<IActionResult> Resubmit(
        string clientCompanyId, [FromBody] CreateSubAccountRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken cancellationToken)
    {
        var scope = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var command = new ResubmitEntityRegistrationCommand(
            scope, idempotencyKey, request.BusinessName, request.BusinessUniqueIdentifier,
            request.IdentifierIssuingCountryCode, request.Country, request.State, request.City, request.Postcode,
            request.StreetName, request.BuildingNumber, HttpContext.TraceIdentifier);

        var result = await resubmitHandler.HandleAsync(command, cancellationToken);
        return Ok(result);
    }
}

public sealed record CreateSubAccountRequest(
    string TargetClientCompanyId, string BusinessName, string BusinessUniqueIdentifier,
    string IdentifierIssuingCountryCode, string Country, string State, string City, string Postcode,
    string StreetName, string BuildingNumber);

public sealed record SetSubAccountDisabledRequest(bool Disabled);
```

Update `CreateSubAccountCommand` to keep field name `ClientCompanyId` as the **target** tenant being created (Admin is the caller, but Admin has no `ClientCompanyId` of its own — the command's `ClientCompanyId` is unambiguous as "the tenant being provisioned"). Remove the old route-vs-tenant equality check and try/catch block entirely — Task 2's `TreasuryProblemDetailsExceptionHandler` now maps every thrown exception.

Update `Program.cs`: register `IQueryHandler<GetSubAccountQuery, GetSubAccountResult>` → `GetSubAccountQueryHandler`, `IQueryHandler<ListSubAccountsQuery, IReadOnlyList<GetSubAccountResult>>` → `ListSubAccountsQueryHandler`, `ICommandHandler<SetSubAccountDisabledCommand, GetSubAccountResult>` → `SetSubAccountDisabledCommandHandler`, `ICommandHandler<ResubmitEntityRegistrationCommand, GetSubAccountResult>` → `ResubmitEntityRegistrationCommandHandler`, and `IEntityRegistrationRepository` → `EntityRegistrationRepository` (Task 3).

- [ ] **Step 6: Update existing integration tests for the new route and Admin-only create**

Update `SubAccountsCrossTenantCreationTests`, `SubAccountsDuplicateClientConflictTests`, `SubAccountsIdempotencyReplayTests` (grep for `clients/{` route usage and `POST .../sub-account` calls): switch to `POST /api/v1/sub-accounts` with an `Admin`-role caller header and `TargetClientCompanyId` in the body; assert `403` (`tenant-forbidden`) when a `SubAccount`-role caller attempts `Create`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet build`
Expected: 0 warnings.

Run: `dotnet test`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: admin-only sub-account create, get/list/disable/enable/resubmit endpoints"
```

---

## Task 5: Webhook pipeline core (durable inbox, dedup, per-topic processors)

`WebhookInboxEntry` exists in the schema today (`WebhookInbox` table, unique index on `CircleEventId`) but is **never written to** — `CircleWebhooksController` currently parses the SNS envelope and calls `ProcessExternalEntityDecisionHandler` directly, with no durable record and no dedup, and silently no-ops every topic except `externalEntities`. This task makes the inbox real per PRD §10: every verified webhook delivery is persisted before any side effect, deduped by provider event id, dispatched to a per-topic processor, and its outcome (Processed/Failed) recorded.

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/WebhookInboxEntry.cs` (delete; superseded by the Application-layer type below)
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/WebhookInboxEntry.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/WebhookProcessingStatus.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/IWebhookInboxRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/WebhookInboxRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/IWebhookTopicProcessor.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/IncomingWebhookEvent.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/WebhookProcessor.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/ExternalEntitiesWebhookTopicProcessor.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Controllers/CircleWebhooksController.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Delete + regenerate: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*` (`InitialCreate`)
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Webhooks/WebhookProcessorTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/WebhookDedupTests.cs`

**Interfaces:**
- Consumes: `ICommandHandler<ProcessExternalEntityDecisionCommand, ProcessExternalEntityDecisionResult>` (existing, Task 3), `IUnitOfWork` (existing).
- Produces: `IncomingWebhookEvent(string Topic, string ProviderEventId, string PayloadJson)`; `IWebhookTopicProcessor { string Topic { get; } Task ProcessAsync(string payloadJson, CancellationToken ct); }`; `IWebhookInboxRepository { Task<bool> TryAddAsync(WebhookInboxEntry entry, CancellationToken ct); Task MarkProcessedAsync(Guid id, CancellationToken ct); Task MarkFailedAsync(Guid id, string error, CancellationToken ct); }`; `WebhookProcessor.HandleAsync(IncomingWebhookEvent evt, CancellationToken ct) : Task<WebhookProcessingStatus>`. Tasks 6 and 8-11 register additional `IWebhookTopicProcessor` implementations for `deposits`, `transfers`, `payouts`, `addressBookRecipients` via plain DI registration — no change to `WebhookProcessor` itself is required to add them.

- [ ] **Step 1: Write the failing test for `WebhookProcessor`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Webhooks/WebhookProcessorTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Webhooks;

public class WebhookProcessorTests
{
    [Fact]
    public async Task Marks_entry_Processed_when_topic_processor_succeeds()
    {
        var inbox = Substitute.For<IWebhookInboxRepository>();
        inbox.TryAddAsync(Arg.Any<WebhookInboxEntry>(), TestContext.Current.CancellationToken).Returns(true);
        var topicProcessor = Substitute.For<IWebhookTopicProcessor>();
        topicProcessor.Topic.Returns("externalEntities");

        var processor = new WebhookProcessor(inbox, [topicProcessor]);

        var status = await processor.HandleAsync(
            new IncomingWebhookEvent("externalEntities", "evt-1", "{}"), TestContext.Current.CancellationToken);

        Assert.Equal(WebhookProcessingStatus.Processed, status);
        await topicProcessor.Received(1).ProcessAsync("{}", TestContext.Current.CancellationToken);
        await inbox.Received(1).MarkProcessedAsync(Arg.Any<Guid>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Returns_Processed_without_reprocessing_when_event_id_already_seen()
    {
        var inbox = Substitute.For<IWebhookInboxRepository>();
        inbox.TryAddAsync(Arg.Any<WebhookInboxEntry>(), TestContext.Current.CancellationToken).Returns(false);
        var topicProcessor = Substitute.For<IWebhookTopicProcessor>();
        topicProcessor.Topic.Returns("externalEntities");

        var processor = new WebhookProcessor(inbox, [topicProcessor]);

        var status = await processor.HandleAsync(
            new IncomingWebhookEvent("externalEntities", "evt-1", "{}"), TestContext.Current.CancellationToken);

        Assert.Equal(WebhookProcessingStatus.Processed, status);
        await topicProcessor.DidNotReceive().ProcessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Marks_entry_Failed_when_no_topic_processor_registered()
    {
        var inbox = Substitute.For<IWebhookInboxRepository>();
        inbox.TryAddAsync(Arg.Any<WebhookInboxEntry>(), TestContext.Current.CancellationToken).Returns(true);

        var processor = new WebhookProcessor(inbox, []);

        var status = await processor.HandleAsync(
            new IncomingWebhookEvent("unknownTopic", "evt-2", "{}"), TestContext.Current.CancellationToken);

        Assert.Equal(WebhookProcessingStatus.Failed, status);
        await inbox.Received(1).MarkFailedAsync(Arg.Any<Guid>(), Arg.Any<string>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Marks_entry_Failed_when_topic_processor_throws()
    {
        var inbox = Substitute.For<IWebhookInboxRepository>();
        inbox.TryAddAsync(Arg.Any<WebhookInboxEntry>(), TestContext.Current.CancellationToken).Returns(true);
        var topicProcessor = Substitute.For<IWebhookTopicProcessor>();
        topicProcessor.Topic.Returns("externalEntities");
        topicProcessor.ProcessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));

        var processor = new WebhookProcessor(inbox, [topicProcessor]);

        var status = await processor.HandleAsync(
            new IncomingWebhookEvent("externalEntities", "evt-3", "{}"), TestContext.Current.CancellationToken);

        Assert.Equal(WebhookProcessingStatus.Failed, status);
        await inbox.Received(1).MarkFailedAsync(Arg.Any<Guid>(), "boom", TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*WebhookProcessorTests*"`
Expected: FAIL — `WebhookProcessor`, `IWebhookInboxRepository`, `WebhookInboxEntry`, `IncomingWebhookEvent` don't exist yet in `Application.Webhooks`.

- [ ] **Step 3: Add the inbox entry type, status enum, and port contracts**

Delete `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/WebhookInboxEntry.cs` — the type moves to `Application` so `IWebhookInboxRepository` can reference it without `Application` depending on `Infrastructure` (EF Core maps POCOs from any referenced assembly, so this is a pure move, not a functional change).

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/WebhookInboxEntry.cs
namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class WebhookInboxEntry
{
    public Guid Id { get; set; }
    public required string Topic { get; set; }
    public required string CircleEventId { get; set; }
    public required string PayloadJson { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public bool Processed { get; set; }
    public string? ProcessingResult { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/WebhookProcessingStatus.cs
namespace TreasuryServiceOrchestrator.Application.Webhooks;

public enum WebhookProcessingStatus
{
    Processed,
    Failed,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/IWebhookInboxRepository.cs
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface IWebhookInboxRepository
{
    Task<bool> TryAddAsync(WebhookInboxEntry entry, CancellationToken cancellationToken);
    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/IWebhookTopicProcessor.cs
namespace TreasuryServiceOrchestrator.Application.Webhooks;

public interface IWebhookTopicProcessor
{
    string Topic { get; }
    Task ProcessAsync(string payloadJson, CancellationToken cancellationToken);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/IncomingWebhookEvent.cs
namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed record IncomingWebhookEvent(string Topic, string ProviderEventId, string PayloadJson);
```

- [ ] **Step 4: Implement `WebhookProcessor`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/WebhookProcessor.cs
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class WebhookProcessor(
    IWebhookInboxRepository inbox, IEnumerable<IWebhookTopicProcessor> topicProcessors)
{
    public async Task<WebhookProcessingStatus> HandleAsync(
        IncomingWebhookEvent incoming, CancellationToken cancellationToken = default)
    {
        var entry = new WebhookInboxEntry
        {
            Id = Guid.NewGuid(),
            Topic = incoming.Topic,
            CircleEventId = incoming.ProviderEventId,
            PayloadJson = incoming.PayloadJson,
            ReceivedAtUtc = DateTime.UtcNow,
            Processed = false,
        };

        var inserted = await inbox.TryAddAsync(entry, cancellationToken);
        if (!inserted)
        {
            // Already-seen provider event id (at-least-once redelivery); ack without reprocessing.
            return WebhookProcessingStatus.Processed;
        }

        var topicProcessor = topicProcessors.FirstOrDefault(p =>
            string.Equals(p.Topic, incoming.Topic, StringComparison.Ordinal));

        if (topicProcessor is null)
        {
            await inbox.MarkFailedAsync(entry.Id, $"No processor registered for topic '{incoming.Topic}'.", cancellationToken);
            return WebhookProcessingStatus.Failed;
        }

        try
        {
            await topicProcessor.ProcessAsync(incoming.PayloadJson, cancellationToken);
            await inbox.MarkProcessedAsync(entry.Id, cancellationToken);
            return WebhookProcessingStatus.Processed;
        }
        catch (Exception ex)
        {
            await inbox.MarkFailedAsync(entry.Id, ex.Message, cancellationToken);
            return WebhookProcessingStatus.Failed;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*WebhookProcessorTests*"`
Expected: PASS (4 tests).

- [ ] **Step 6: Implement `WebhookInboxRepository` and the `externalEntities` topic processor**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/WebhookInboxRepository.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

internal sealed class WebhookInboxRepository(TreasuryServiceOrchestratorDbContext dbContext) : IWebhookInboxRepository
{
    public async Task<bool> TryAddAsync(WebhookInboxEntry entry, CancellationToken cancellationToken)
    {
        dbContext.WebhookInbox.Add(entry);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await dbContext.WebhookInbox.SingleAsync(e => e.Id == id, cancellationToken);
        entry.Processed = true;
        entry.ProcessingResult = "Processed";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        var entry = await dbContext.WebhookInbox.SingleAsync(e => e.Id == id, cancellationToken);
        entry.Attempts += 1;
        entry.ProcessingResult = "Failed";
        entry.LastError = error;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

This repository calls `SaveChangesAsync` three times across a delivery's lifetime (insert, then mark-processed-or-failed) rather than going through `IUnitOfWork`'s two-phase pattern — the inbox insert must commit *before* the topic processor runs, so a crash mid-processing still leaves a durable, re-checkable record. `internal sealed` matches the visibility of other Infrastructure-only repository implementations in this codebase.

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/ExternalEntitiesWebhookTopicProcessor.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using TreasuryServiceOrchestrator.Application.SubAccounts;

namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class ExternalEntitiesWebhookTopicProcessor(
    ICommandHandler<ProcessExternalEntityDecisionCommand, ProcessExternalEntityDecisionResult> decisionHandler)
    : IWebhookTopicProcessor
{
    public string Topic => "externalEntities";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ExternalEntitiesPayload>(payloadJson)
            ?? throw new InvalidOperationException("externalEntities webhook payload was empty or malformed.");

        if (payload.ExternalEntity?.WalletId is null || payload.ExternalEntity.ComplianceState is null)
            throw new InvalidOperationException("externalEntities webhook payload missing walletId or complianceState.");

        await decisionHandler.HandleAsync(
            new ProcessExternalEntityDecisionCommand(payload.ExternalEntity.WalletId, payload.ExternalEntity.ComplianceState),
            cancellationToken);
    }

    private sealed record ExternalEntitiesPayload(
        [property: JsonPropertyName("externalEntity")] ExternalEntityPayload? ExternalEntity);

    private sealed record ExternalEntityPayload(
        [property: JsonPropertyName("walletId")] string? WalletId,
        [property: JsonPropertyName("complianceState")] string? ComplianceState);
}
```

The nested `externalEntity` shape matches the existing `CircleNotificationMessage`/`CircleExternalEntity` payload already produced by `CircleWebhooksController` today — this processor receives that same JSON body verbatim as `payloadJson`.

- [ ] **Step 7: Rework `CircleWebhooksController` to persist-then-dispatch through `WebhookProcessor`**

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/CircleWebhooksController.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using TreasuryServiceOrchestrator.Api.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks;
using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks/circle")]
public sealed class CircleWebhooksController(
    ISnsMessageVerifier verifier, WebhookProcessor webhookProcessor, ILogger<CircleWebhooksController> logger)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        CircleWebhookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CircleWebhookEnvelope>(rawBody);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null)
        {
            return BadRequest();
        }

        if (!await verifier.VerifyAsync(envelope, cancellationToken))
        {
            logger.LogWarning(
                "Rejected Circle webhook with unverifiable SNS signature (MessageId {MessageId})", envelope.MessageId);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (envelope.Type == "SubscriptionConfirmation")
        {
            logger.LogInformation(
                "SNS SubscriptionConfirmation received for topic {TopicArn}; confirm manually via SubscribeURL: {SubscribeURL}",
                envelope.TopicArn, envelope.SubscribeURL);
            return Ok();
        }

        var notificationType = ParseNotificationType(envelope.Message);
        if (notificationType is null || string.IsNullOrEmpty(envelope.Message))
        {
            logger.LogInformation(
                "Verified Circle webhook with unrecognized body received (MessageId {MessageId})", envelope.MessageId);
            return Ok();
        }

        var status = await webhookProcessor.HandleAsync(
            new IncomingWebhookEvent(notificationType, envelope.MessageId, envelope.Message), cancellationToken);

        if (status == WebhookProcessingStatus.Failed)
        {
            // Non-2xx so SNS redelivers; the inbox row already recorded the failure and attempt count.
            logger.LogWarning(
                "Failed to process {NotificationType} webhook (MessageId {MessageId}); requesting SNS redelivery",
                notificationType, envelope.MessageId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        logger.LogInformation(
            "Processed {NotificationType} webhook (MessageId {MessageId})", notificationType, envelope.MessageId);
        return Ok();
    }

    private static string? ParseNotificationType(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        try
        {
            return JsonSerializer.Deserialize<NotificationTypeEnvelope>(message)?.NotificationType;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record NotificationTypeEnvelope(
        [property: JsonPropertyName("notificationType")] string? NotificationType);
}
```

The previous `catch (SubAccountNotFoundException) → 404` special case is removed: `ExternalEntitiesWebhookTopicProcessor` lets that exception propagate, `WebhookProcessor` catches it generically and marks the inbox row `Failed`, and the controller's uniform 500-on-`Failed` response already triggers SNS redelivery — the same outcome, now handled once in `WebhookProcessor` instead of per-topic in the controller.

- [ ] **Step 8: Wire up DbContext mapping and DI registrations**

In `TreasuryServiceOrchestratorDbContext.cs`, change the `using` for `WebhookInboxEntry` from `TreasuryServiceOrchestrator.Infrastructure.Persistence` (its own namespace, now removed) to `TreasuryServiceOrchestrator.Application.Webhooks`, keep `public DbSet<WebhookInboxEntry> WebhookInbox => Set<WebhookInboxEntry>();`, and extend the existing `OnModelCreating` block for it:

```csharp
modelBuilder.Entity<WebhookInboxEntry>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => e.CircleEventId).IsUnique();
    entity.Property(e => e.Topic).HasMaxLength(64);
});
```

In `Program.cs`, add:
```csharp
builder.Services.AddScoped<IWebhookInboxRepository, WebhookInboxRepository>();
builder.Services.AddScoped<IWebhookTopicProcessor, ExternalEntitiesWebhookTopicProcessor>();
builder.Services.AddScoped<WebhookProcessor>();
```
(Tasks 6 and 8-11 add further `AddScoped<IWebhookTopicProcessor, ...>()` lines for the mock emitter, `deposits`, `transfers`, `payouts`, `addressBookRecipients` — no other change to this wiring is needed.)

- [ ] **Step 9: Regenerate `InitialCreate`**

```bash
rm src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*InitialCreate*.cs src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/TreasuryServiceOrchestratorDbContextModelSnapshot.cs
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
```
Diff-check: `WebhookInbox` table gains `Topic nvarchar(64)`, `Attempts int`, `LastError nvarchar(max)` columns; `IX_WebhookInbox_CircleEventId` unique index unchanged.

- [ ] **Step 10: Write the integration dedup test**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/WebhookDedupTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class WebhookDedupTests(SqlServerTestDatabaseFixture fixture) : IClassFixture<SqlServerTestDatabaseFixture>
{
    [Fact]
    public async Task Redelivered_message_id_is_not_reprocessed()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "SubAccount";
        });
        using var client = factory.CreateClient();

        var envelope = new
        {
            Type = "Notification",
            MessageId = "dedup-test-1",
            Message = "{\"notificationType\":\"externalEntities\",\"externalEntity\":{\"walletId\":\"wallet-does-not-exist\",\"complianceState\":\"approved\"}}",
        };
        var envelopeJson = JsonSerializer.Serialize(envelope);

        var first = await client.PostAsync("/api/v1/webhooks/circle",
            new StringContent(envelopeJson, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        var second = await client.PostAsync("/api/v1/webhooks/circle",
            new StringContent(envelopeJson, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        // First delivery fails (no matching SubAccount for the wallet) and requests redelivery via 500;
        // second delivery with the same MessageId must be deduped rather than attempted again.
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
```

If `TreasuryServiceOrchestratorWebApplicationFactory`'s constructor shape differs from the two-argument form used above, match the shape already used by `CallerContextTests` (Task 1) instead of introducing a new overload.

- [ ] **Step 11: Run tests to verify they pass**

Run: `dotnet build`
Expected: 0 warnings.

Run: `dotnet test`
Expected: all green.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat: durable webhook inbox with dedup and per-topic processor dispatch"
```

---

## Task 6: Mock provider gateway + simulated webhook emitter (PRD §13)

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderOptions.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockModeGuard.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/IMockRandomSource.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/ScheduledMockWebhook.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/IMockWebhookScheduler.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockWebhookChannel.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockWebhookDispatcher.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockWebhookDispatchBackgroundService.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.json`
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.Development.json`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockModeGuardTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockWebhookDispatcherTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockSubAccountGatewayTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockStablecoinGatewayTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/MockProviderWiringTests.cs`

**Interfaces:**
- Consumes: Task 5's `IWebhookInboxRepository`, `IWebhookTopicProcessor`, `WebhookProcessor`, `IncomingWebhookEvent(string Topic, string ProviderEventId, string PayloadJson)`. Task 3's `ISubAccountGateway.CreateExternalEntityAsync(CreateExternalEntityGatewayRequest, CancellationToken) : Task<CreateExternalEntityResult>` / `GetExternalEntityAsync(string walletId, CancellationToken) : Task<ExternalEntityStatusResult>` and the existing `IStablecoinGateway.RedeemAsync(RedeemGatewayRequest, CancellationToken) : Task<GatewayRedeemResult>` / `GetTransferStatusAsync(string transferId, CancellationToken) : Task<TransferStatusResult>` (all from `TreasuryServiceOrchestrator.Application.Ports.GatewayDtos`). Task 3's `EntityRegistrationStatusMapper.Map(string)`, which requires `ComplianceState` values to be `"Pending"`/`"Accepted"`/`"Rejected"` (case-insensitive) — **not** the old stub's uppercase `"ACCEPTED"`/`"REJECTED"`. `ProviderUnavailableException(string message)` from `TreasuryServiceOrchestrator.Application.Exceptions`.
- Produces: `MockProviderOptions { bool Enabled; int WebhookDelayMilliseconds; int ResponseLatencyMilliseconds; double FailureInjectionRate; string RejectBusinessNameSuffix; }` bound to config section `"MockProvider"`. `MockModeGuard.Validate(bool mockModeEnabled, string environmentName)` — throws `InvalidOperationException` if `mockModeEnabled` and `environmentName == Environments.Production`. `IMockWebhookScheduler.Schedule(ScheduledMockWebhook webhook)` — later tasks (8-11: deposits, transfers, payouts, addressBookRecipients mock emission) inject this to schedule simulated webhooks through the same Task 5 pipeline; no other wiring changes needed when they do.

- [ ] **Step 1: Write the failing test for `MockModeGuard`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockModeGuardTests.cs
using Microsoft.Extensions.Hosting;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public class MockModeGuardTests
{
    [Fact]
    public void Validate_Throws_When_Enabled_In_Production()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => MockModeGuard.Validate(mockModeEnabled: true, environmentName: Environments.Production));

        Assert.Contains("Production", ex.Message);
    }

    [Theory]
    [InlineData(Environments.Development)]
    [InlineData("Sandbox")]
    [InlineData(Environments.Staging)]
    public void Validate_Does_Not_Throw_When_Enabled_Outside_Production(string environmentName)
    {
        MockModeGuard.Validate(mockModeEnabled: true, environmentName: environmentName);
    }

    [Fact]
    public void Validate_Does_Not_Throw_When_Disabled_In_Production()
    {
        MockModeGuard.Validate(mockModeEnabled: false, environmentName: Environments.Production);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockModeGuardTests"`
Expected: FAIL to compile — `MockModeGuard` does not exist.

- [ ] **Step 3: Implement `MockModeGuard` and `MockProviderOptions`**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderOptions.cs
namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed class MockProviderOptions
{
    public bool Enabled { get; set; }

    public int WebhookDelayMilliseconds { get; set; } = 500;

    public int ResponseLatencyMilliseconds { get; set; }

    public double FailureInjectionRate { get; set; }

    public string RejectBusinessNameSuffix { get; set; } = "REJECTME";
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockModeGuard.cs
using Microsoft.Extensions.Hosting;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public static class MockModeGuard
{
    public static void Validate(bool mockModeEnabled, string environmentName)
    {
        if (mockModeEnabled && string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "MockProvider:Enabled cannot be true when the environment is Production. " +
                "Mock mode is structurally disallowed in production (PRD §13).");
        }
    }
}
```

`MockProviderOptions` is deliberately `public` (unlike the `Circle` namespace types) — it must be bound from `IConfiguration` in `Program.cs`, which the `LayeringTests.cs` Circle-non-public rule does not restrict since this is the `Mocks` namespace, not `Circle`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockModeGuardTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Write the failing test for the mock webhook scheduler + dispatcher**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockWebhookDispatcherTests.cs
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public class MockWebhookDispatcherTests
{
    private sealed class SpyTopicProcessor : IWebhookTopicProcessor
    {
        public string Topic => "externalEntities";
        public string? LastPayloadJson { get; private set; }

        public Task ProcessAsync(string payloadJson, CancellationToken ct)
        {
            LastPayloadJson = payloadJson;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInboxRepository : IWebhookInboxRepository
    {
        public Task<bool> TryAddAsync(string topic, string providerEventId, string payloadJson, CancellationToken ct)
            => Task.FromResult(true);

        public Task MarkProcessedAsync(string topic, string providerEventId, CancellationToken ct)
            => Task.CompletedTask;

        public Task MarkFailedAsync(string topic, string providerEventId, string error, CancellationToken ct)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task DispatchOneAsync_Delivers_Scheduled_Webhook_Through_WebhookProcessor()
    {
        var spy = new SpyTopicProcessor();
        var services = new ServiceCollection();
        services.AddScoped<IWebhookInboxRepository, FakeInboxRepository>();
        services.AddScoped<IWebhookTopicProcessor>(_ => spy);
        services.AddScoped<WebhookProcessor>();
        await using var provider = services.BuildServiceProvider();

        var channel = new MockWebhookChannel();
        var dispatcher = new MockWebhookDispatcher(channel, provider.GetRequiredService<IServiceScopeFactory>());

        channel.Schedule(new ScheduledMockWebhook(
            "externalEntities",
            "{\"externalEntity\":{\"walletId\":\"wallet-1\",\"complianceState\":\"Accepted\"}}",
            TimeSpan.Zero));

        var dispatched = await dispatcher.DispatchOneAsync(TestContext.Current.CancellationToken);

        Assert.True(dispatched);
        Assert.Equal(
            "{\"externalEntity\":{\"walletId\":\"wallet-1\",\"complianceState\":\"Accepted\"}}",
            spy.LastPayloadJson);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockWebhookDispatcherTests"`
Expected: FAIL to compile — `MockWebhookChannel`, `ScheduledMockWebhook`, `MockWebhookDispatcher` do not exist.

- [ ] **Step 7: Implement the scheduler and dispatcher**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/ScheduledMockWebhook.cs
namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed record ScheduledMockWebhook(string Topic, string PayloadJson, TimeSpan Delay);
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/IMockWebhookScheduler.cs
namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public interface IMockWebhookScheduler
{
    void Schedule(ScheduledMockWebhook webhook);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockWebhookChannel.cs
using System.Threading.Channels;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed class MockWebhookChannel : IMockWebhookScheduler
{
    private readonly Channel<ScheduledMockWebhook> _channel = Channel.CreateUnbounded<ScheduledMockWebhook>();

    public ChannelReader<ScheduledMockWebhook> Reader => _channel.Reader;

    public void Schedule(ScheduledMockWebhook webhook) => _channel.Writer.TryWrite(webhook);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockWebhookDispatcher.cs
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Webhooks;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed class MockWebhookDispatcher(MockWebhookChannel channel, IServiceScopeFactory scopeFactory)
{
    public async Task<bool> DispatchOneAsync(CancellationToken ct)
    {
        if (!await channel.Reader.WaitToReadAsync(ct))
        {
            return false;
        }

        if (!channel.Reader.TryRead(out var scheduled))
        {
            return false;
        }

        if (scheduled.Delay > TimeSpan.Zero)
        {
            await Task.Delay(scheduled.Delay, ct);
        }

        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<WebhookProcessor>();
        await processor.HandleAsync(
            new IncomingWebhookEvent(scheduled.Topic, $"mock-{Guid.NewGuid():N}", scheduled.PayloadJson),
            ct);

        return true;
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockWebhookDispatchBackgroundService.cs
using Microsoft.Extensions.Hosting;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed class MockWebhookDispatchBackgroundService(MockWebhookDispatcher dispatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await dispatcher.DispatchOneAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on host shutdown.
        }
    }
}
```

`WebhookInboxEntry`/`IWebhookInboxRepository`/`IWebhookTopicProcessor`/`WebhookProcessor`/`IncomingWebhookEvent` all live in `TreasuryServiceOrchestrator.Application.Webhooks` per Task 5 — add `using TreasuryServiceOrchestrator.Application.Webhooks;` where the test file references them if not already implied above.

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockWebhookDispatcherTests"`
Expected: PASS.

- [ ] **Step 9: Write the failing tests for the mock gateways**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockSubAccountGatewayTests.cs
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public class MockSubAccountGatewayTests
{
    private sealed class CapturingScheduler : IMockWebhookScheduler
    {
        public ScheduledMockWebhook? Captured { get; private set; }
        public void Schedule(ScheduledMockWebhook webhook) => Captured = webhook;
    }

    private sealed class FixedRandomSource(double value) : IMockRandomSource
    {
        public double NextDouble() => value;
    }

    private static MockSubAccountGateway CreateSut(
        CapturingScheduler scheduler,
        MockProviderOptions? options = null,
        IMockRandomSource? randomSource = null)
        => new(
            Options.Create(options ?? new MockProviderOptions()),
            scheduler,
            randomSource ?? new FixedRandomSource(1.0));

    [Fact]
    public async Task CreateExternalEntityAsync_Returns_Pending_And_Schedules_Accepted_Webhook_For_Normal_Name()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler);

        var result = await sut.CreateExternalEntityAsync(
            new CreateExternalEntityGatewayRequest(
                "Acme Corp",
                "123456789",
                "US",
                new ExternalEntityAddress("US", "NY", "New York", "10001", "Main St", "1")),
            TestContext.Current.CancellationToken);

        Assert.Equal("Pending", result.ComplianceState);
        Assert.NotNull(scheduler.Captured);
        Assert.Equal("externalEntities", scheduler.Captured!.Topic);
        Assert.Contains("\"complianceState\":\"Accepted\"", scheduler.Captured.PayloadJson);
        Assert.Contains(result.WalletId, scheduler.Captured.PayloadJson);
    }

    [Fact]
    public async Task CreateExternalEntityAsync_Schedules_Rejected_Webhook_For_Magic_Suffix_Name()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler);

        await sut.CreateExternalEntityAsync(
            new CreateExternalEntityGatewayRequest(
                "Acme Corp REJECTME",
                "123456789",
                "US",
                new ExternalEntityAddress("US", "NY", "New York", "10001", "Main St", "1")),
            TestContext.Current.CancellationToken);

        Assert.Contains("\"complianceState\":\"Rejected\"", scheduler.Captured!.PayloadJson);
    }

    [Fact]
    public async Task CreateExternalEntityAsync_Throws_ProviderUnavailable_When_Failure_Injected()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(
            scheduler,
            options: new MockProviderOptions { FailureInjectionRate = 1.0 },
            randomSource: new FixedRandomSource(0.0));

        await Assert.ThrowsAsync<ProviderUnavailableException>(() => sut.CreateExternalEntityAsync(
            new CreateExternalEntityGatewayRequest(
                "Acme Corp",
                "123456789",
                "US",
                new ExternalEntityAddress("US", "NY", "New York", "10001", "Main St", "1")),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetExternalEntityAsync_Returns_The_State_Recorded_At_Creation()
    {
        var scheduler = new CapturingScheduler();
        var sut = CreateSut(scheduler);

        var created = await sut.CreateExternalEntityAsync(
            new CreateExternalEntityGatewayRequest(
                "Acme Corp REJECTME",
                "123456789",
                "US",
                new ExternalEntityAddress("US", "NY", "New York", "10001", "Main St", "1")),
            TestContext.Current.CancellationToken);

        var status = await sut.GetExternalEntityAsync(created.WalletId, TestContext.Current.CancellationToken);

        Assert.Equal("Rejected", status.ComplianceState);
    }
}
```

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Mocks/MockStablecoinGatewayTests.cs
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Mocks;

public class MockStablecoinGatewayTests
{
    private sealed class FixedRandomSource(double value) : IMockRandomSource
    {
        public double NextDouble() => value;
    }

    [Fact]
    public async Task RedeemAsync_Returns_Complete_With_Fiat_Amount_In_Target_Currency()
    {
        var sut = new MockStablecoinGateway(Options.Create(new MockProviderOptions()), new FixedRandomSource(1.0));

        var result = await sut.RedeemAsync(
            new RedeemGatewayRequest("idem-1", new Money(100m, "USDC"), "USD"),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Complete, result.Status);
        Assert.Equal(100m, result.FiatAmount.Amount);
        Assert.Equal("USD", result.FiatAmount.CurrencyCode);
    }

    [Fact]
    public async Task RedeemAsync_Throws_ProviderUnavailable_When_Failure_Injected()
    {
        var sut = new MockStablecoinGateway(
            Options.Create(new MockProviderOptions { FailureInjectionRate = 1.0 }),
            new FixedRandomSource(0.0));

        await Assert.ThrowsAsync<ProviderUnavailableException>(() => sut.RedeemAsync(
            new RedeemGatewayRequest("idem-1", new Money(100m, "USDC"), "USD"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTransferStatusAsync_Returns_The_Status_Recorded_At_Redemption()
    {
        var sut = new MockStablecoinGateway(Options.Create(new MockProviderOptions()), new FixedRandomSource(1.0));

        var redeemed = await sut.RedeemAsync(
            new RedeemGatewayRequest("idem-1", new Money(100m, "USDC"), "USD"),
            TestContext.Current.CancellationToken);
        var status = await sut.GetTransferStatusAsync(redeemed.CircleRedeemId, TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Complete, status.Status);
    }
}
```

- [ ] **Step 10: Run tests to verify they fail**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockSubAccountGatewayTests|*MockStablecoinGatewayTests"`
Expected: FAIL to compile — `MockSubAccountGateway`, `MockStablecoinGateway`, `IMockRandomSource` do not exist.

- [ ] **Step 11: Implement the mock gateways**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/IMockRandomSource.cs
namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public interface IMockRandomSource
{
    double NextDouble();
}

internal sealed class SystemRandomSource : IMockRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed class MockSubAccountGateway(
    IOptions<MockProviderOptions> options,
    IMockWebhookScheduler webhookScheduler,
    IMockRandomSource randomSource) : ISubAccountGateway
{
    private readonly ConcurrentDictionary<string, string> _complianceStateByWalletId = new();

    public async Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken ct)
    {
        var settings = options.Value;

        if (settings.ResponseLatencyMilliseconds > 0)
        {
            await Task.Delay(settings.ResponseLatencyMilliseconds, ct);
        }

        if (settings.FailureInjectionRate > 0 && randomSource.NextDouble() < settings.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated a 5xx failure.");
        }

        var walletId = Guid.NewGuid().ToString("N");
        var finalState = request.BusinessName.EndsWith(settings.RejectBusinessNameSuffix, StringComparison.OrdinalIgnoreCase)
            ? "Rejected"
            : "Accepted";
        _complianceStateByWalletId[walletId] = finalState;

        var payload = $"{{\"externalEntity\":{{\"walletId\":\"{walletId}\",\"complianceState\":\"{finalState}\"}}}}";
        webhookScheduler.Schedule(new ScheduledMockWebhook(
            "externalEntities",
            payload,
            TimeSpan.FromMilliseconds(settings.WebhookDelayMilliseconds)));

        return new CreateExternalEntityResult(walletId, "Pending", request.BusinessName, request.BusinessUniqueIdentifier);
    }

    public Task<ExternalEntityStatusResult> GetExternalEntityAsync(string walletId, CancellationToken ct)
    {
        var state = _complianceStateByWalletId.TryGetValue(walletId, out var s) ? s : "Pending";
        return Task.FromResult(new ExternalEntityStatusResult(walletId, state));
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed class MockStablecoinGateway(
    IOptions<MockProviderOptions> options,
    IMockRandomSource randomSource) : IStablecoinGateway
{
    private readonly ConcurrentDictionary<string, TransferStatus> _statusByTransferId = new();

    public async Task<GatewayRedeemResult> RedeemAsync(RedeemGatewayRequest request, CancellationToken ct)
    {
        var settings = options.Value;

        if (settings.ResponseLatencyMilliseconds > 0)
        {
            await Task.Delay(settings.ResponseLatencyMilliseconds, ct);
        }

        if (settings.FailureInjectionRate > 0 && randomSource.NextDouble() < settings.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated a 5xx failure.");
        }

        var transferId = Guid.NewGuid().ToString("N");
        _statusByTransferId[transferId] = TransferStatus.Complete;

        return new GatewayRedeemResult(
            transferId,
            new Money(request.UsdcAmount.Amount, request.TargetFiatCurrencyCode),
            TransferStatus.Complete);
    }

    public Task<TransferStatusResult> GetTransferStatusAsync(string transferId, CancellationToken ct)
    {
        var status = _statusByTransferId.TryGetValue(transferId, out var s) ? s : TransferStatus.Pending;
        return Task.FromResult(new TransferStatusResult(transferId, status));
    }
}
```

`MockSubAccountGateway` and `MockStablecoinGateway` hold in-memory state (`ConcurrentDictionary`) that must survive across requests, so they are registered as singletons in Step 13 below — unlike the Circle stubs, which are stateless and scoped.

- [ ] **Step 12: Run tests to verify they pass**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockSubAccountGatewayTests|*MockStablecoinGatewayTests"`
Expected: PASS (7 tests total across both classes).

- [ ] **Step 13: Wire conditional DI registration in `Program.cs`**

Replace the existing unconditional gateway registrations (`builder.Services.AddScoped<IStablecoinGateway, CircleMintGateway>();` and `builder.Services.AddScoped<ISubAccountGateway, CircleSubAccountGateway>();`) with:

```csharp
var mockProviderOptions = builder.Configuration.GetSection("MockProvider").Get<MockProviderOptions>()
    ?? new MockProviderOptions();
MockModeGuard.Validate(mockProviderOptions.Enabled, builder.Environment.EnvironmentName);
builder.Services.Configure<MockProviderOptions>(builder.Configuration.GetSection("MockProvider"));

if (mockProviderOptions.Enabled)
{
    builder.Services.AddSingleton<IMockRandomSource, SystemRandomSource>();
    builder.Services.AddSingleton<MockWebhookChannel>();
    builder.Services.AddSingleton<IMockWebhookScheduler>(sp => sp.GetRequiredService<MockWebhookChannel>());
    builder.Services.AddSingleton<MockWebhookDispatcher>();
    builder.Services.AddHostedService<MockWebhookDispatchBackgroundService>();
    builder.Services.AddSingleton<IStablecoinGateway, MockStablecoinGateway>();
    builder.Services.AddSingleton<ISubAccountGateway, MockSubAccountGateway>();
}
else
{
    builder.Services.AddScoped<IStablecoinGateway, CircleMintGateway>();
    builder.Services.AddScoped<ISubAccountGateway, CircleSubAccountGateway>();
}
```

Add `using TreasuryServiceOrchestrator.Infrastructure.Mocks;` to `Program.cs`'s using block. This registration runs before the `AddScoped<IWebhookInboxRepository, ...>()`/`AddScoped<WebhookProcessor>()` lines from Task 5 — order does not matter since these are independent registrations, but keep the gateway block together for readability. `MockModeGuard.Validate` runs unconditionally at startup (not just when `mockProviderOptions.Enabled` is true) so a misconfigured `Production` environment with `MockProvider:Enabled=true` fails host startup immediately rather than silently running with mock providers.

- [ ] **Step 14: Add default `MockProvider` config**

In `src/TreasuryServiceOrchestrator.Api/appsettings.json`, add (base default is `false` — safe in every environment, including any future `Production`/`Sandbox` overlay that forgets to set it explicitly):

```json
"MockProvider": {
  "Enabled": false,
  "WebhookDelayMilliseconds": 500,
  "ResponseLatencyMilliseconds": 0,
  "FailureInjectionRate": 0,
  "RejectBusinessNameSuffix": "REJECTME"
}
```

In `src/TreasuryServiceOrchestrator.Api/appsettings.Development.json`, add (Phase 1 has no real Circle credentials, so local/dev runs default to mock mode):

```json
"MockProvider": {
  "Enabled": true
}
```

- [ ] **Step 15: Write the integration tests for DI wiring and the production guard**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/MockProviderWiringTests.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class MockProviderWiringTests
{
    [Fact]
    public void Mock_Gateways_Are_Registered_When_MockProvider_Enabled_In_Development()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("MockProvider:Enabled", "true");
        });

        using var scope = factory.Services.CreateScope();

        Assert.IsType<MockStablecoinGateway>(scope.ServiceProvider.GetRequiredService<IStablecoinGateway>());
        Assert.IsType<MockSubAccountGateway>(scope.ServiceProvider.GetRequiredService<ISubAccountGateway>());
    }

    [Fact]
    public void Host_Startup_Throws_When_MockProvider_Enabled_In_Production()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("MockProvider:Enabled", "true");
        });

        Assert.Throws<InvalidOperationException>(() => factory.Services);
    }
}
```

If `WebApplicationFactory<Program>`'s constructor/configuration shape differs from the form used above (e.g. it needs the same connection-string or `KnownClientCompanies` overrides other integration test classes pass into `WithWebHostBuilder`), match whichever shape `CallerContextTests` (Task 1) or `WebhookDedupTests` (Task 5) already use instead of introducing a new one — those two tests only add the one extra `MockProvider:Enabled` setting on top of that shape.

- [ ] **Step 16: Run tests to verify they pass**

Run: `dotnet build`
Expected: 0 warnings.

Run: `dotnet test`
Expected: all green.

- [ ] **Step 17: Commit**

```bash
git add -A
git commit -m "feat: mock provider gateways with simulated webhooks and production guard"
```

---

## Task 7: Deposit address generation + list (PRD §6.1)

Adds "generate a permanent deposit address for a sub-account on a given (chain, currency)" and "list this sub-account's deposit addresses" (PRD §3.1: *"A blockchain deposit address generated for the wallet, per (chain, currency). Permanent — the provider does not support rotation or expiry."*). Repeated requests for the same (chain, currency) return the existing address — this is itself the idempotency guarantee, so this task does **not** use the two-`SaveChangesAsync` idempotency-key pattern from Task 1's Global Constraints; the `(SubAccountId, Chain, Currency)` unique index is the dedup key, and the handler is a plain find-or-create with one `SaveChangesAsync`.

**Chain modeling decision:** `docs/PRD.md` never enumerates supported chains (only USDC is named as currency; the Roadmap defers "EURC + additional chains", implying v1 supports some unnamed single chain), and `docs/circle-mint-docs/` has no deposit-address page to cross-check chain codes against. Rather than hardcode a guessed chain literal, `Chain` is a free-form string validated against a configured allow-list (`SupportedChainsOptions`, bound from `appsettings.json` `SupportedChains`), defaulting to `["ETH"]` for Phase 1. **Before Phase 3's real Circle HTTP integration, cross-check this default and the allow-list mechanism against the live `docs/circle-mint-docs` deposit-address page (verify its live nav path first per `docs/circle-mint-docs/README.md`'s refresh-notes process) — the real Circle API may reject or spell chain codes differently.**

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Domain/DepositAddress.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IDepositAddressRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Shared/SupportedChainsOptions.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Compliance/Ports/ISubAccountGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressResult.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressCommandValidator.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/DepositAddresses/ListDepositAddressesQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/DepositAddresses/ListDepositAddressesQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/DepositAddressRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Delete + regenerate: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*` (`InitialCreate`)
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.json`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/DepositAddressesController.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/DepositAddresses/GenerateDepositAddressCommandHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/DepositAddresses/ListDepositAddressesQueryHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/DepositAddressesEndpointsTests.cs`

**Interfaces:**
- Consumes: `ICallerContext` (Task 1), `TenantScopeResolver.Resolve` (Task 2), `ISubAccountRepository.GetByClientCompanyIdAsync` (existing), `SubAccountLifecycleState` (Task 3), exception types `NotFoundException`/`ConflictException` (Task 2), `ISubAccountGateway` (extended this task — both `CircleSubAccountGateway` (Task-0 baseline) and `MockSubAccountGateway` (Task 6) must implement the new method).
- Produces: `class DepositAddress { Guid Id; Guid SubAccountId; string ClientCompanyId; string Chain; string Currency; string Address; DateTime CreatedAtUtc; }`; `IDepositAddressRepository { Task<DepositAddress?> FindAsync(Guid subAccountId, string chain, string currency, CancellationToken ct); Task AddAsync(DepositAddress address, CancellationToken ct); Task<IReadOnlyList<DepositAddress>> ListForSubAccountAsync(Guid subAccountId, CancellationToken ct); }`; `SupportedChainsOptions : List<string>`; `GenerateDepositAddressGatewayRequest(string WalletId, string Chain, string Currency)`; `GenerateDepositAddressResult(string Address, string Chain, string Currency)` (gateway DTO — same shape name as the Application-layer `DepositAddresses.GenerateDepositAddressResult` command result but a distinct type in `Ports` namespace vs `DepositAddresses` namespace); `GenerateDepositAddressCommand(string ResolvedClientCompanyId, string Chain, string Currency, string CorrelationId)`; `DepositAddresses.GenerateDepositAddressResult(Guid DepositAddressId, string Chain, string Currency, string Address, bool WasExisting)`; `ListDepositAddressesQuery(string ResolvedClientCompanyId)`. Task 8's ledger/deposit-crediting rework will look up the owning `SubAccount` from an inbound deposit's destination address via `IDepositAddressRepository` (a method to resolve by `Address` is deferred to Task 8 since it isn't needed until the crediting workflow consumes it).

- [ ] **Step 1: Write the failing unit tests for the command handler**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/DepositAddresses/GenerateDepositAddressCommandHandlerTests.cs
using Microsoft.Extensions.Options;
using NSubstitute;
using TreasuryServiceOrchestrator.Application.DepositAddresses;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.DepositAddresses;

public class GenerateDepositAddressCommandHandlerTests
{
    private static SubAccount ActiveSubAccount(string clientCompanyId) => new()
    {
        Id = Guid.NewGuid(),
        ClientCompanyId = clientCompanyId,
        CircleWalletId = "wallet-1",
        LifecycleState = SubAccountLifecycleState.Active,
        IsDisabled = false,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static GenerateDepositAddressCommandHandler CreateSut(
        ISubAccountRepository subAccounts,
        IDepositAddressRepository depositAddresses,
        ISubAccountGateway gateway,
        IAuditLogService auditLog,
        IUnitOfWork unitOfWork)
    {
        var options = Options.Create(new SupportedChainsOptions { "ETH" });
        var validator = new GenerateDepositAddressCommandValidator(options);
        return new GenerateDepositAddressCommandHandler(
            subAccounts, depositAddresses, gateway, auditLog, unitOfWork, validator);
    }

    [Fact]
    public async Task Generates_new_address_when_none_exists_for_chain_and_currency()
    {
        var subAccount = ActiveSubAccount("acme");
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        var depositAddresses = Substitute.For<IDepositAddressRepository>();
        depositAddresses.FindAsync(subAccount.Id, "ETH", "USDC", Arg.Any<CancellationToken>())
            .Returns((DepositAddress?)null);
        var gateway = Substitute.For<ISubAccountGateway>();
        gateway.GenerateDepositAddressAsync(
                Arg.Is<GenerateDepositAddressGatewayRequest>(r => r.WalletId == "wallet-1" && r.Chain == "ETH" && r.Currency == "USDC"),
                Arg.Any<CancellationToken>())
            .Returns(new GenerateDepositAddressResult("0xabc123", "ETH", "USDC"));
        var sut = CreateSut(subAccounts, depositAddresses, gateway, Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        var result = await sut.HandleAsync(
            new GenerateDepositAddressCommand("acme", "ETH", "USDC", "corr-1"), TestContext.Current.CancellationToken);

        Assert.Equal("0xabc123", result.Address);
        Assert.False(result.WasExisting);
        await depositAddresses.Received(1).AddAsync(
            Arg.Is<DepositAddress>(a => a.Address == "0xabc123" && a.SubAccountId == subAccount.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_existing_address_without_calling_gateway_when_one_already_exists()
    {
        var subAccount = ActiveSubAccount("acme");
        var existing = new DepositAddress
        {
            Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme",
            Chain = "ETH", Currency = "USDC", Address = "0xexisting", CreatedAtUtc = DateTime.UtcNow,
        };
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        var depositAddresses = Substitute.For<IDepositAddressRepository>();
        depositAddresses.FindAsync(subAccount.Id, "ETH", "USDC", Arg.Any<CancellationToken>()).Returns(existing);
        var gateway = Substitute.For<ISubAccountGateway>();
        var sut = CreateSut(subAccounts, depositAddresses, gateway, Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        var result = await sut.HandleAsync(
            new GenerateDepositAddressCommand("acme", "ETH", "USDC", "corr-2"), TestContext.Current.CancellationToken);

        Assert.Equal("0xexisting", result.Address);
        Assert.True(result.WasExisting);
        await gateway.DidNotReceive().GenerateDepositAddressAsync(Arg.Any<GenerateDepositAddressGatewayRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_NotFound_when_sub_account_does_not_exist()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("ghost", Arg.Any<CancellationToken>()).Returns((SubAccount?)null);
        var sut = CreateSut(
            subAccounts, Substitute.For<IDepositAddressRepository>(), Substitute.For<ISubAccountGateway>(),
            Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        await Assert.ThrowsAsync<NotFoundException>(() => sut.HandleAsync(
            new GenerateDepositAddressCommand("ghost", "ETH", "USDC", "corr-3"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_Conflict_when_sub_account_is_not_active()
    {
        var subAccount = ActiveSubAccount("acme");
        subAccount.LifecycleState = SubAccountLifecycleState.PendingCompliance;
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        var sut = CreateSut(
            subAccounts, Substitute.For<IDepositAddressRepository>(), Substitute.For<ISubAccountGateway>(),
            Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        await Assert.ThrowsAsync<ConflictException>(() => sut.HandleAsync(
            new GenerateDepositAddressCommand("acme", "ETH", "USDC", "corr-4"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_ValidationException_for_unsupported_chain()
    {
        var subAccount = ActiveSubAccount("acme");
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        var sut = CreateSut(
            subAccounts, Substitute.For<IDepositAddressRepository>(), Substitute.For<ISubAccountGateway>(),
            Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => sut.HandleAsync(
            new GenerateDepositAddressCommand("acme", "SOL", "USDC", "corr-5"), TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*GenerateDepositAddressCommandHandlerTests*"`
Expected: FAIL — compile errors, none of the new types exist yet.

- [ ] **Step 3: Add domain entity, config options, and repository port**

```csharp
// src/TreasuryServiceOrchestrator.Domain/DepositAddress.cs
namespace TreasuryServiceOrchestrator.Domain;

public class DepositAddress
{
    public Guid Id { get; set; }
    public required Guid SubAccountId { get; set; }
    public required string ClientCompanyId { get; set; }
    public required string Chain { get; set; }
    public required string Currency { get; set; }
    public required string Address { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Shared/SupportedChainsOptions.cs
namespace TreasuryServiceOrchestrator.Application.Shared;

public sealed class SupportedChainsOptions : List<string>;
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IDepositAddressRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IDepositAddressRepository
{
    Task<DepositAddress?> FindAsync(Guid subAccountId, string chain, string currency, CancellationToken cancellationToken = default);
    Task AddAsync(DepositAddress address, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DepositAddress>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Extend gateway DTOs and `ISubAccountGateway`**

Append to `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`:
```csharp
public sealed record GenerateDepositAddressGatewayRequest(
    string WalletId,
    string Chain,
    string Currency);

public sealed record GenerateDepositAddressResult(
    string Address,
    string Chain,
    string Currency);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Compliance/Ports/ISubAccountGateway.cs
namespace TreasuryServiceOrchestrator.Application.Compliance.Ports;

public interface ISubAccountGateway
{
    Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken);

    Task<ExternalEntityStatusResult> GetExternalEntityAsync(
        string walletId, CancellationToken cancellationToken);

    Task<GenerateDepositAddressResult> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Implement the new gateway method on both `CircleSubAccountGateway` and `MockSubAccountGateway`**

Add to `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs` (inside the existing `CircleSubAccountGateway` class, after `GetExternalEntityAsync`):
```csharp
    public Task<GenerateDepositAddressResult> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new GenerateDepositAddressResult(
            Address: $"0x{Guid.NewGuid():N}",
            Chain: request.Chain,
            Currency: request.Currency));
```

Add to `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs` (inside the existing `MockSubAccountGateway` class, after `GetExternalEntityAsync`):
```csharp
    public async Task<GenerateDepositAddressResult> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken ct)
    {
        var settings = options.Value;

        if (settings.ResponseLatencyMilliseconds > 0)
        {
            await Task.Delay(settings.ResponseLatencyMilliseconds, ct);
        }

        if (settings.FailureInjectionRate > 0 && randomSource.NextDouble() < settings.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated a 5xx failure.");
        }

        return new GenerateDepositAddressResult($"0x{Guid.NewGuid():N}", request.Chain, request.Currency);
    }
```
The mock gateway generates a fresh address on every call — dedup against an already-issued address for the same (chain, currency) is the Application-layer handler's job (Step 7 below), matching how Task 3's `EntityRegistration`/`SubAccount` split keeps provider-facing generation separate from local dedup state.

- [ ] **Step 6: Add the command, validator, and result types**

```csharp
// src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressCommand.cs
namespace TreasuryServiceOrchestrator.Application.DepositAddresses;

public sealed record GenerateDepositAddressCommand(
    string ResolvedClientCompanyId,
    string Chain,
    string Currency,
    string CorrelationId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressResult.cs
namespace TreasuryServiceOrchestrator.Application.DepositAddresses;

public sealed record GenerateDepositAddressResult(
    Guid DepositAddressId,
    string Chain,
    string Currency,
    string Address,
    bool WasExisting);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressCommandValidator.cs
using FluentValidation;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Shared;

namespace TreasuryServiceOrchestrator.Application.DepositAddresses;

public sealed class GenerateDepositAddressCommandValidator : AbstractValidator<GenerateDepositAddressCommand>
{
    public GenerateDepositAddressCommandValidator(IOptions<SupportedChainsOptions> supportedChains)
    {
        var allowed = supportedChains.Value;

        RuleFor(c => c.Chain)
            .NotEmpty()
            .Must(chain => allowed.Contains(chain, StringComparer.OrdinalIgnoreCase))
            .WithMessage(c => $"Chain '{c.Chain}' is not supported. Supported chains: {string.Join(", ", allowed)}.");

        RuleFor(c => c.Currency).NotEmpty();
        RuleFor(c => c.ResolvedClientCompanyId).NotEmpty();
    }
}
```

- [ ] **Step 7: Implement `GenerateDepositAddressCommandHandler`**

```csharp
// src/TreasuryServiceOrchestrator.Application/DepositAddresses/GenerateDepositAddressCommandHandler.cs
using System.Text.Json;
using FluentValidation;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.DepositAddresses;

public sealed class GenerateDepositAddressCommandHandler(
    ISubAccountRepository subAccounts,
    IDepositAddressRepository depositAddresses,
    ISubAccountGateway gateway,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    IValidator<GenerateDepositAddressCommand> validator)
    : ICommandHandler<GenerateDepositAddressCommand, GenerateDepositAddressResult>
{
    public async Task<GenerateDepositAddressResult> HandleAsync(
        GenerateDepositAddressCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for '{command.ResolvedClientCompanyId}'.");

        if (subAccount.LifecycleState != SubAccountLifecycleState.Active || subAccount.IsDisabled)
            throw new ConflictException(
                $"Sub-account '{command.ResolvedClientCompanyId}' is not active (state: {subAccount.LifecycleState}, disabled: {subAccount.IsDisabled}).");

        var existing = await depositAddresses.FindAsync(subAccount.Id, command.Chain, command.Currency, cancellationToken);
        if (existing is not null)
        {
            return new GenerateDepositAddressResult(existing.Id, existing.Chain, existing.Currency, existing.Address, WasExisting: true);
        }

        var gatewayResult = await gateway.GenerateDepositAddressAsync(
            new GenerateDepositAddressGatewayRequest(subAccount.CircleWalletId!, command.Chain, command.Currency),
            cancellationToken);

        var depositAddress = new DepositAddress
        {
            Id = Guid.NewGuid(),
            SubAccountId = subAccount.Id,
            ClientCompanyId = command.ResolvedClientCompanyId,
            Chain = gatewayResult.Chain,
            Currency = gatewayResult.Currency,
            Address = gatewayResult.Address,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await depositAddresses.AddAsync(depositAddress, cancellationToken);

        await auditLog.AppendAsync(
            "DepositAddressGenerated", "DepositAddress", depositAddress.Id.ToString(),
            JsonSerializer.Serialize(new { depositAddress.Chain, depositAddress.Currency, depositAddress.Address }),
            command.ResolvedClientCompanyId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GenerateDepositAddressResult(depositAddress.Id, depositAddress.Chain, depositAddress.Currency, depositAddress.Address, WasExisting: false);
    }
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*GenerateDepositAddressCommandHandlerTests*"`
Expected: PASS (5 tests)

- [ ] **Step 9: Write the failing unit test for the list query handler, then implement it**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/DepositAddresses/ListDepositAddressesQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.DepositAddresses;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.DepositAddresses;

public class ListDepositAddressesQueryHandlerTests
{
    [Fact]
    public async Task Returns_all_addresses_for_the_resolved_sub_account()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var addresses = new List<DepositAddress>
        {
            new() { Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme", Chain = "ETH", Currency = "USDC", Address = "0xabc", CreatedAtUtc = DateTime.UtcNow },
        };
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        var depositAddresses = Substitute.For<IDepositAddressRepository>();
        depositAddresses.ListForSubAccountAsync(subAccount.Id, Arg.Any<CancellationToken>()).Returns(addresses);
        var sut = new ListDepositAddressesQueryHandler(subAccounts, depositAddresses);

        var result = await sut.HandleAsync(new ListDepositAddressesQuery("acme"), TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("0xabc", result[0].Address);
    }

    [Fact]
    public async Task Throws_NotFound_when_sub_account_does_not_exist()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("ghost", Arg.Any<CancellationToken>()).Returns((SubAccount?)null);
        var sut = new ListDepositAddressesQueryHandler(subAccounts, Substitute.For<IDepositAddressRepository>());

        await Assert.ThrowsAsync<NotFoundException>(() => sut.HandleAsync(
            new ListDepositAddressesQuery("ghost"), TestContext.Current.CancellationToken));
    }
}
```

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListDepositAddressesQueryHandlerTests*"`
Expected: FAIL — compile error, types don't exist yet.

```csharp
// src/TreasuryServiceOrchestrator.Application/DepositAddresses/ListDepositAddressesQuery.cs
namespace TreasuryServiceOrchestrator.Application.DepositAddresses;

public sealed record ListDepositAddressesQuery(string ResolvedClientCompanyId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/DepositAddresses/ListDepositAddressesQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.DepositAddresses;

public sealed class ListDepositAddressesQueryHandler(
    ISubAccountRepository subAccounts,
    IDepositAddressRepository depositAddresses)
    : IQueryHandler<ListDepositAddressesQuery, IReadOnlyList<DepositAddress>>
{
    public async Task<IReadOnlyList<DepositAddress>> HandleAsync(
        ListDepositAddressesQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for '{query.ResolvedClientCompanyId}'.");

        return await depositAddresses.ListForSubAccountAsync(subAccount.Id, cancellationToken);
    }
}
```

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListDepositAddressesQueryHandlerTests*"`
Expected: PASS (2 tests)

- [ ] **Step 10: Add the EF repository and DbContext mapping**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/DepositAddressRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class DepositAddressRepository(TreasuryServiceOrchestratorDbContext dbContext) : IDepositAddressRepository
{
    public Task<DepositAddress?> FindAsync(Guid subAccountId, string chain, string currency, CancellationToken cancellationToken = default)
        => dbContext.DepositAddresses.SingleOrDefaultAsync(
            a => a.SubAccountId == subAccountId && a.Chain == chain && a.Currency == currency, cancellationToken);

    public async Task AddAsync(DepositAddress address, CancellationToken cancellationToken = default)
        => await dbContext.DepositAddresses.AddAsync(address, cancellationToken);

    public async Task<IReadOnlyList<DepositAddress>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken = default)
        => await dbContext.DepositAddresses.Where(a => a.SubAccountId == subAccountId).ToListAsync(cancellationToken);
}
```

In `TreasuryServiceOrchestratorDbContext.cs`, add `public DbSet<DepositAddress> DepositAddresses => Set<DepositAddress>();` next to `Deposits`, and in `OnModelCreating` add:
```csharp
modelBuilder.Entity<DepositAddress>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
    entity.HasIndex(e => new { e.SubAccountId, e.Chain, e.Currency })
        .IsUnique()
        .HasDatabaseName("IX_DepositAddresses_SubAccountId_Chain_Currency");
});
```

- [ ] **Step 11: Wire DI and config**

In `Program.cs`, add near the other repository registrations:
```csharp
builder.Services.AddScoped<IDepositAddressRepository, DepositAddressRepository>();
builder.Services.Configure<SupportedChainsOptions>(builder.Configuration.GetSection("SupportedChains"));
```
Add `using TreasuryServiceOrchestrator.Application.DepositAddresses;` if the assembly-scanned validator registration needs it (check the existing `AddValidatorsFromAssemblyContaining<...>()` call already covers the `Application` assembly — no new scan registration needed since `GenerateDepositAddressCommandValidator` lives in that assembly).

In `src/TreasuryServiceOrchestrator.Api/appsettings.json`, add at the top level:
```json
"SupportedChains": [ "ETH" ]
```

- [ ] **Step 12: Regenerate `InitialCreate`**

```bash
rm src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*InitialCreate*.cs src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/TreasuryServiceOrchestratorDbContextModelSnapshot.cs
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
```
Expected: new migration creates a `DepositAddresses` table with the `IX_DepositAddresses_SubAccountId_Chain_Currency` unique index.

- [ ] **Step 13: Add the controller**

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/DepositAddressesController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.DepositAddresses;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/deposit-addresses")]
public sealed class DepositAddressesController(
    ICallerContext caller,
    ICommandHandler<GenerateDepositAddressCommand, GenerateDepositAddressResult> generateHandler,
    IQueryHandler<ListDepositAddressesQuery, IReadOnlyList<TreasuryServiceOrchestrator.Domain.DepositAddress>> listHandler)
    : ControllerBase
{
    public sealed record GenerateDepositAddressRequest(string Chain, string Currency);

    [HttpPost]
    public async Task<IActionResult> Generate(
        string clientCompanyId, [FromBody] GenerateDepositAddressRequest request, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var correlationId = HttpContext.TraceIdentifier;

        var result = await generateHandler.HandleAsync(
            new GenerateDepositAddressCommand(resolved, request.Chain, request.Currency, correlationId), cancellationToken);

        return result.WasExisting ? Ok(result) : CreatedAtAction(nameof(List), new { clientCompanyId }, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(string clientCompanyId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;

        var result = await listHandler.HandleAsync(new ListDepositAddressesQuery(resolved), cancellationToken);

        return Ok(result);
    }
}
```

Check `src/TreasuryServiceOrchestrator.Api/Program.cs` for how existing `ICommandHandler<,>`/`IQueryHandler<,>` implementations are registered (grep `AddScoped<ICommandHandler` / `AddScoped<IQueryHandler`) and add matching lines for `GenerateDepositAddressCommandHandler` and `ListDepositAddressesQueryHandler` in the same style.

- [ ] **Step 14: Write the integration test**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/DepositAddressesEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class DepositAddressesEndpointsTests(SqlServerTestDatabaseFixture fixture) : IClassFixture<SqlServerTestDatabaseFixture>
{
    [Fact]
    public async Task Generate_then_list_returns_the_same_permanent_address()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "SubAccount";
            config["SupportedChains:0"] = "ETH";
            config["MockProvider:Enabled"] = "true";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");

        await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co", new
        {
            BusinessName = "Acme Co", BusinessUniqueIdentifier = "US-EIN-123", IdentifierIssuingCountryCode = "US",
            Country = "US", State = "NY", City = "New York", Postcode = "10001", StreetName = "Main St", BuildingNumber = "1",
            IdempotencyKey = Guid.NewGuid().ToString(),
        }, TestContext.Current.CancellationToken);

        var first = await client.PostAsJsonAsync(
            "/api/v1/sub-accounts/acme-co/deposit-addresses",
            new { Chain = "ETH", Currency = "USDC" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<GenerateDepositAddressResponse>(TestContext.Current.CancellationToken);

        var second = await client.PostAsJsonAsync(
            "/api/v1/sub-accounts/acme-co/deposit-addresses",
            new { Chain = "ETH", Currency = "USDC" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<GenerateDepositAddressResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(firstBody!.Address, secondBody!.Address);

        var list = await client.GetFromJsonAsync<List<GenerateDepositAddressResponse>>(
            "/api/v1/sub-accounts/acme-co/deposit-addresses", TestContext.Current.CancellationToken);
        Assert.Single(list!);
    }

    private sealed record GenerateDepositAddressResponse(Guid DepositAddressId, string Chain, string Currency, string Address, bool WasExisting);
}
```

This test requires the sub-account's registration to reach `Active` synchronously enough for the deposit-address call to see `SubAccountLifecycleState.Active`. Under `MockProvider:Enabled=true`, `MockSubAccountGateway.CreateExternalEntityAsync` (Task 6) returns a business name **not** ending in `REJECTME`, which schedules an `externalEntities` webhook with `complianceState: "Accepted"` after `WebhookDelayMilliseconds` (500ms default) — if this test is flaky, check whether the webhook dispatcher (Task 5/6) has processed that webhook before the deposit-address call runs; add a short retry-poll on `GET /api/v1/sub-accounts/acme-co` (Task 4) for `LifecycleState == "Active"` before calling the deposit-address endpoint if needed, following whatever polling helper (if any) `SubAccountsEndpointsTests` (Task 4) already established for this same race.

- [ ] **Step 15: Run full test suite**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 16: Commit**

```bash
git add -A
git commit -m "feat: deposit address generation and listing per (chain, currency)"
```

---

## Task 8: Ledger — `Transaction` + `BalanceSnapshot` (PRD §6.2, §6.3, §9)

Supersedes the pre-existing `Deposit` entity and `ProcessDepositCommandHandler` (audit-only records, no ledger/balance-history model) with the PRD §9.1 ledger: *"Every transaction the service initiates, and every provider webhook event, produces/updates a ledger `Transaction` row"* and *"A `BalanceSnapshot` is recorded per wallet on a schedule and after every ledger mutation."* Also wires the `deposits` webhook topic into `CircleWebhooksController` (currently unrouted — falls through to a no-op `Ok()`) and adds the PRD §9.2 list/get transaction and balance endpoints. Task 11 (redemption rework) and Task 10 (outbound transfers) will each add their own `TransactionType` case onto this same ledger rather than inventing a parallel one.

**Files:**
- Delete: `src/TreasuryServiceOrchestrator.Domain/Deposit.cs`
- Delete: `src/TreasuryServiceOrchestrator.Application/Deposits/ProcessDepositCommand.cs`, `ProcessDepositResult.cs`, `ProcessDepositValidator.cs`, `ProcessDepositCommandHandler.cs`, `SubAccountNotReadyException.cs`
- Delete: `src/TreasuryServiceOrchestrator.Application/Ports/IDepositRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/TransactionType.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/TransactionStatus.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/Transaction.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/BalanceSnapshotReason.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/BalanceSnapshot.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ITransactionRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IBalanceSnapshotRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IDepositAddressRepository.cs` (add `FindByAddressAsync`)
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositResult.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommandValidator.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/DepositSourceNotResolvedException.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/ListTransactionsQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/ListTransactionsQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/GetTransactionQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/GetTransactionQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/GetCurrentBalanceQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/GetCurrentBalanceQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/GetBalanceHistoryQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/GetBalanceHistoryQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TransactionRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/BalanceSnapshotRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/DepositAddressRepository.cs` (add `FindByAddressAsync`)
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Controllers/CircleWebhooksController.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/TransactionsController.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/BalancesController.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Delete + regenerate: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*` (`InitialCreate`)
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/ProcessDepositCommandHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/ListTransactionsQueryHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/GetCurrentBalanceQueryHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/DepositWebhookLedgerTests.cs`
- Delete: any existing test files under `tests/TreasuryServiceOrchestrator.UnitTests/Application/Deposits/`

**Interfaces:**
- Consumes: `ISubAccountRepository` (existing, extended nowhere further this task), `IFundAccountRepository` (existing), `IDepositAddressRepository.FindByAddressAsync` (added this task), `IIdempotencyService`/`IdempotencyExecutor.ExecuteAsync` (existing pattern), `IAuditLogService.AppendAsync` (existing), `IUnitOfWork.SaveChangesAsync` (existing), `ICallerContext`/`TenantScopeResolver.Resolve` (Tasks 1-2), `SubAccountLifecycleState` (Task 3).
- Produces: `enum TransactionType { Deposit, Transfer, Redemption }`; `enum TransactionStatus { Pending, Complete, Failed }`; `class Transaction { Guid Id; Guid SubAccountId; string ClientCompanyId; TransactionType Type; TransactionStatus Status; Money Amount; string ProviderReferenceId; DepositSourceType? DepositSourceType; string? FailureReason; string CorrelationId; DateTime CreatedAtUtc; DateTime UpdatedAtUtc; }`; `enum BalanceSnapshotReason { PostMutation, Scheduled }`; `class BalanceSnapshot { Guid Id; Guid SubAccountId; string ClientCompanyId; Money Balance; BalanceSnapshotReason Reason; DateTime CapturedAtUtc; }`; `ITransactionRepository`/`IBalanceSnapshotRepository` (signatures below); `ProcessDepositCommand(string? CircleWalletId, string? DestinationAddress, string CircleReferenceId, DepositSourceType SourceType, Money Amount, string CorrelationId)`; `ProcessDepositResult(Guid TransactionId, TransactionStatus Status, Money FundAccountBalance)`. Task 10 and Task 11 will construct their own `Transaction` rows via `ITransactionRepository.AddAsync` directly (no shared "record ledger entry" helper is introduced — YAGNI until a second caller shows the actual duplication).

- [ ] **Step 1: Locate and delete the superseded `Deposit` artifacts**

```bash
grep -rl "IDepositRepository" src/TreasuryServiceOrchestrator.Infrastructure
```
Expected output: one file, `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/DepositRepository.cs` — delete it along with the files listed under "Delete" above.

```bash
rm src/TreasuryServiceOrchestrator.Domain/Deposit.cs
rm src/TreasuryServiceOrchestrator.Application/Deposits/ProcessDepositCommand.cs
rm src/TreasuryServiceOrchestrator.Application/Deposits/ProcessDepositResult.cs
rm src/TreasuryServiceOrchestrator.Application/Deposits/ProcessDepositValidator.cs
rm src/TreasuryServiceOrchestrator.Application/Deposits/ProcessDepositCommandHandler.cs
rm src/TreasuryServiceOrchestrator.Application/Deposits/SubAccountNotReadyException.cs
rm src/TreasuryServiceOrchestrator.Application/Ports/IDepositRepository.cs
rm src/TreasuryServiceOrchestrator.Infrastructure/Persistence/DepositRepository.cs
rm -rf tests/TreasuryServiceOrchestrator.UnitTests/Application/Deposits
```
Also remove the `AddScoped<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>, ProcessDepositCommandHandler>()` and `IDepositRepository` DI lines from `Program.cs` — grep first: `grep -n "ProcessDepositCommand\|IDepositRepository" src/TreasuryServiceOrchestrator.Api/Program.cs`, then delete those exact lines.

- [ ] **Step 2: Add the domain types**

```csharp
// src/TreasuryServiceOrchestrator.Domain/TransactionType.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum TransactionType
{
    Deposit,
    Transfer,
    Redemption,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/TransactionStatus.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum TransactionStatus
{
    Pending,
    Complete,
    Failed,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/Transaction.cs
namespace TreasuryServiceOrchestrator.Domain;

public class Transaction
{
    public Guid Id { get; set; }
    public required Guid SubAccountId { get; set; }
    public required string ClientCompanyId { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public Money Amount { get; set; }
    public required string ProviderReferenceId { get; set; }
    public DepositSourceType? DepositSourceType { get; set; }
    public string? FailureReason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/BalanceSnapshotReason.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum BalanceSnapshotReason
{
    PostMutation,
    Scheduled,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/BalanceSnapshot.cs
namespace TreasuryServiceOrchestrator.Domain;

public class BalanceSnapshot
{
    public Guid Id { get; set; }
    public required Guid SubAccountId { get; set; }
    public required string ClientCompanyId { get; set; }
    public Money Balance { get; set; }
    public BalanceSnapshotReason Reason { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}
```

`DepositSourceType` already exists (referenced by the pre-existing `Deposit.cs`) — keep that enum file as-is; only `DepositStatus` (superseded by `TransactionStatus`) should be deleted if it has no other consumers: `grep -rl "DepositStatus" src/` and delete `src/TreasuryServiceOrchestrator.Domain/DepositStatus.cs` if the only remaining hit is its own declaration.

- [ ] **Step 3: Add repository ports**

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ITransactionRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<Transaction?> GetByProviderReferenceIdAsync(string providerReferenceId, CancellationToken cancellationToken = default);
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> ListAsync(
        Guid subAccountId,
        TransactionType? type,
        TransactionStatus? status,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IBalanceSnapshotRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IBalanceSnapshotRepository
{
    Task AddAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<BalanceSnapshot?> GetLatestAsync(Guid subAccountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BalanceSnapshot>> GetHistoryAsync(
        Guid subAccountId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
```

Append to `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IDepositAddressRepository.cs` (inside the existing interface):
```csharp
    Task<DepositAddress?> FindByAddressAsync(string address, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Write the failing unit tests for `ProcessDepositCommandHandler`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/ProcessDepositCommandHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger;

public class ProcessDepositCommandHandlerTests
{
    private static SubAccount ActiveSubAccount(string clientCompanyId, string walletId = "wallet-1") => new()
    {
        Id = Guid.NewGuid(), ClientCompanyId = clientCompanyId, CircleWalletId = walletId,
        LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };

    private sealed record Fixture(
        ISubAccountRepository SubAccounts, IFundAccountRepository FundAccounts,
        ITransactionRepository Transactions, IBalanceSnapshotRepository Snapshots,
        IDepositAddressRepository DepositAddresses, IIdempotencyService Idempotency,
        IAuditLogService AuditLog, IUnitOfWork UnitOfWork, ProcessDepositCommandHandler Sut);

    private static Fixture CreateFixture()
    {
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdempotencyCheckResult(IdempotencyOutcome.Miss, null));
        var subAccounts = Substitute.For<ISubAccountRepository>();
        var fundAccounts = Substitute.For<IFundAccountRepository>();
        var transactions = Substitute.For<ITransactionRepository>();
        var snapshots = Substitute.For<IBalanceSnapshotRepository>();
        var depositAddresses = Substitute.For<IDepositAddressRepository>();
        var auditLog = Substitute.For<IAuditLogService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = new ProcessDepositCommandHandler(
            subAccounts, fundAccounts, transactions, snapshots, depositAddresses,
            idempotency, auditLog, unitOfWork, new ProcessDepositCommandValidator());
        return new Fixture(subAccounts, fundAccounts, transactions, snapshots, depositAddresses, idempotency, auditLog, unitOfWork, sut);
    }

    [Fact]
    public async Task Credits_fund_account_and_records_a_complete_transaction_and_snapshot()
    {
        var f = CreateFixture();
        var subAccount = ActiveSubAccount("acme");
        f.SubAccounts.GetByCircleWalletIdAsync("wallet-1", Arg.Any<CancellationToken>()).Returns(subAccount);
        f.FundAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns((FundAccount?)null);

        var result = await f.Sut.HandleAsync(
            new ProcessDepositCommand("wallet-1", null, "circle-ref-1", DepositSourceType.FiatWire, new Money(100m, "USD"), "corr-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransactionStatus.Complete, result.Status);
        Assert.Equal(100m, result.FundAccountBalance.Amount);
        await f.Transactions.Received(1).AddAsync(
            Arg.Is<Transaction>(t => t.Type == TransactionType.Deposit && t.Status == TransactionStatus.Complete && t.ProviderReferenceId == "circle-ref-1"),
            Arg.Any<CancellationToken>());
        await f.Snapshots.Received(1).AddAsync(
            Arg.Is<BalanceSnapshot>(s => s.Reason == BalanceSnapshotReason.PostMutation && s.Balance.Amount == 100m),
            Arg.Any<CancellationToken>());
        await f.FundAccounts.Received(1).AddAsync(Arg.Is<FundAccount>(fa => fa.ClientCompanyId == "acme"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Records_a_failed_transaction_on_currency_mismatch_without_crediting_balance()
    {
        var f = CreateFixture();
        var subAccount = ActiveSubAccount("acme");
        var fundAccount = new FundAccount { Id = Guid.NewGuid(), ClientCompanyId = "acme", Balance = 50m, CurrencyCode = "USD", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        f.SubAccounts.GetByCircleWalletIdAsync("wallet-1", Arg.Any<CancellationToken>()).Returns(subAccount);
        f.FundAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(fundAccount);

        var result = await f.Sut.HandleAsync(
            new ProcessDepositCommand("wallet-1", null, "circle-ref-2", DepositSourceType.OnChain, new Money(10m, "EUR"), "corr-2"),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Equal(50m, result.FundAccountBalance.Amount);
        await f.Transactions.Received(1).AddAsync(
            Arg.Is<Transaction>(t => t.Status == TransactionStatus.Failed && t.FailureReason != null), Arg.Any<CancellationToken>());
        await f.Snapshots.DidNotReceive().AddAsync(Arg.Any<BalanceSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolves_sub_account_by_destination_address_when_wallet_id_is_absent()
    {
        var f = CreateFixture();
        var subAccount = ActiveSubAccount("acme");
        f.DepositAddresses.FindByAddressAsync("0xabc", Arg.Any<CancellationToken>())
            .Returns(new DepositAddress { Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme", Chain = "ETH", Currency = "USDC", Address = "0xabc", CreatedAtUtc = DateTime.UtcNow });
        f.SubAccounts.GetByIdAsync(subAccount.Id, Arg.Any<CancellationToken>()).Returns(subAccount);
        f.FundAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns((FundAccount?)null);

        var result = await f.Sut.HandleAsync(
            new ProcessDepositCommand(null, "0xabc", "circle-ref-3", DepositSourceType.OnChain, new Money(25m, "USDC"), "corr-3"),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransactionStatus.Complete, result.Status);
    }

    [Fact]
    public async Task Throws_when_neither_wallet_id_nor_destination_address_resolves()
    {
        var f = CreateFixture();
        f.DepositAddresses.FindByAddressAsync("0xghost", Arg.Any<CancellationToken>()).Returns((DepositAddress?)null);

        await Assert.ThrowsAsync<DepositSourceNotResolvedException>(() => f.Sut.HandleAsync(
            new ProcessDepositCommand(null, "0xghost", "circle-ref-4", DepositSourceType.OnChain, new Money(1m, "USDC"), "corr-4"),
            TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessDepositCommandHandlerTests*"`
Expected: FAIL — none of the `Ledger` namespace types exist yet.

- [ ] **Step 6: Implement the command, validator, exception, and handler**

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommand.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record ProcessDepositCommand(
    string? CircleWalletId,
    string? DestinationAddress,
    string CircleReferenceId,
    DepositSourceType SourceType,
    Money Amount,
    string CorrelationId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositResult.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record ProcessDepositResult(Guid TransactionId, TransactionStatus Status, Money FundAccountBalance);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommandValidator.cs
using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class ProcessDepositCommandValidator : AbstractValidator<ProcessDepositCommand>
{
    public ProcessDepositCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.CircleWalletId) || !string.IsNullOrWhiteSpace(x.DestinationAddress))
            .WithMessage("Either CircleWalletId or DestinationAddress must be provided.");
        RuleFor(x => x.CircleReferenceId).NotEmpty();
        RuleFor(x => x.SourceType).IsInEnum();
        RuleFor(x => x.Amount.Amount).GreaterThan(0);
        RuleFor(x => x.Amount.CurrencyCode).NotEmpty().MaximumLength(4);
        RuleFor(x => x.CorrelationId).NotEmpty();
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/DepositSourceNotResolvedException.cs
namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class DepositSourceNotResolvedException(string? walletId, string? destinationAddress)
    : Exception($"Could not resolve a sub-account for wallet id '{walletId}' / destination address '{destinationAddress}'.")
{
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommandHandler.cs
using System.Text.Json;
using FluentValidation;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class ProcessDepositCommandHandler(
    ISubAccountRepository subAccounts,
    IFundAccountRepository fundAccounts,
    ITransactionRepository transactions,
    IBalanceSnapshotRepository snapshots,
    IDepositAddressRepository depositAddresses,
    IIdempotencyService idempotency,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    IValidator<ProcessDepositCommand> validator)
    : ICommandHandler<ProcessDepositCommand, ProcessDepositResult>
{
    public async Task<ProcessDepositResult> HandleAsync(
        ProcessDepositCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var subAccount = await ResolveSubAccountAsync(command, cancellationToken)
            ?? throw new DepositSourceNotResolvedException(command.CircleWalletId, command.DestinationAddress);

        if (subAccount.LifecycleState != SubAccountLifecycleState.Active || subAccount.IsDisabled)
            throw new ConflictException(
                $"Sub-account '{subAccount.ClientCompanyId}' is not active (state: {subAccount.LifecycleState}, disabled: {subAccount.IsDisabled}).");

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            subAccount.ClientCompanyId,
            $"deposit:{command.CircleReferenceId}",
            new { command.CircleWalletId, command.DestinationAddress, command.CircleReferenceId, command.SourceType, command.Amount },
            unitOfWork,
            async () =>
            {
                var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(subAccount.ClientCompanyId, cancellationToken);
                if (fundAccount is null)
                {
                    fundAccount = new FundAccount
                    {
                        Id = Guid.NewGuid(), ClientCompanyId = subAccount.ClientCompanyId, Balance = 0m,
                        CurrencyCode = command.Amount.CurrencyCode, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                    };
                    await fundAccounts.AddAsync(fundAccount, cancellationToken);
                }

                return !string.Equals(fundAccount.CurrencyCode, command.Amount.CurrencyCode, StringComparison.OrdinalIgnoreCase)
                    ? await RecordFailedAsync(command, subAccount, fundAccount, cancellationToken)
                    : await RecordCompleteAsync(command, subAccount, fundAccount, cancellationToken);
            },
            cancellationToken);
    }

    private Task<SubAccount?> ResolveSubAccountAsync(ProcessDepositCommand command, CancellationToken cancellationToken)
        => !string.IsNullOrWhiteSpace(command.CircleWalletId)
            ? subAccounts.GetByCircleWalletIdAsync(command.CircleWalletId, cancellationToken)
            : ResolveByDestinationAddressAsync(command.DestinationAddress!, cancellationToken);

    private async Task<SubAccount?> ResolveByDestinationAddressAsync(string destinationAddress, CancellationToken cancellationToken)
    {
        var depositAddress = await depositAddresses.FindByAddressAsync(destinationAddress, cancellationToken);
        return depositAddress is null ? null : await subAccounts.GetByIdAsync(depositAddress.SubAccountId, cancellationToken);
    }

    private async Task<ProcessDepositResult> RecordCompleteAsync(
        ProcessDepositCommand command, SubAccount subAccount, FundAccount fundAccount, CancellationToken cancellationToken)
    {
        var transaction = NewTransaction(command, subAccount, TransactionStatus.Complete, failureReason: null);
        await transactions.AddAsync(transaction, cancellationToken);

        fundAccount.Balance += command.Amount.Amount;
        fundAccount.UpdatedAtUtc = DateTime.UtcNow;

        await snapshots.AddAsync(new BalanceSnapshot
        {
            Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = subAccount.ClientCompanyId,
            Balance = new Money(fundAccount.Balance, fundAccount.CurrencyCode),
            Reason = BalanceSnapshotReason.PostMutation, CapturedAtUtc = DateTime.UtcNow,
        }, cancellationToken);

        await auditLog.AppendAsync(
            "DepositCredited", "Transaction", transaction.Id.ToString(),
            JsonSerializer.Serialize(new { transaction.ProviderReferenceId, transaction.DepositSourceType, transaction.Amount, fundAccount.Balance }),
            subAccount.ClientCompanyId, command.CorrelationId, cancellationToken);

        return new ProcessDepositResult(transaction.Id, TransactionStatus.Complete, new Money(fundAccount.Balance, fundAccount.CurrencyCode));
    }

    private async Task<ProcessDepositResult> RecordFailedAsync(
        ProcessDepositCommand command, SubAccount subAccount, FundAccount fundAccount, CancellationToken cancellationToken)
    {
        var failureReason = $"currency-mismatch: fund account currency is {fundAccount.CurrencyCode}, deposit currency is {command.Amount.CurrencyCode}";
        var transaction = NewTransaction(command, subAccount, TransactionStatus.Failed, failureReason);
        await transactions.AddAsync(transaction, cancellationToken);

        await auditLog.AppendAsync(
            "DepositFailed", "Transaction", transaction.Id.ToString(),
            JsonSerializer.Serialize(new { transaction.ProviderReferenceId, transaction.DepositSourceType, transaction.Amount, transaction.FailureReason }),
            subAccount.ClientCompanyId, command.CorrelationId, cancellationToken);

        return new ProcessDepositResult(transaction.Id, TransactionStatus.Failed, new Money(fundAccount.Balance, fundAccount.CurrencyCode));
    }

    private static Transaction NewTransaction(
        ProcessDepositCommand command, SubAccount subAccount, TransactionStatus status, string? failureReason) => new()
    {
        Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = subAccount.ClientCompanyId,
        Type = TransactionType.Deposit, Status = status, Amount = command.Amount,
        ProviderReferenceId = command.CircleReferenceId, DepositSourceType = command.SourceType,
        FailureReason = failureReason, CorrelationId = command.CorrelationId,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessDepositCommandHandlerTests*"`
Expected: PASS (4 tests)

- [ ] **Step 8: Write and implement the list/get transaction and balance query handlers**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/ListTransactionsQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger;

public class ListTransactionsQueryHandlerTests
{
    [Fact]
    public async Task Lists_transactions_for_the_resolved_sub_account()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.ListAsync(subAccount.Id, null, null, null, null, 1, 20, Arg.Any<CancellationToken>())
            .Returns(new List<Transaction>
            {
                new() { Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme", Type = TransactionType.Deposit, Status = TransactionStatus.Complete, Amount = new Money(10m, "USDC"), ProviderReferenceId = "r1", CorrelationId = "c1", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            });
        var sut = new ListTransactionsQueryHandler(subAccounts, transactions);

        var result = await sut.HandleAsync(
            new ListTransactionsQuery("acme", null, null, null, null, 1, 20), TestContext.Current.CancellationToken);

        Assert.Single(result);
    }

    [Fact]
    public async Task Throws_NotFound_when_sub_account_does_not_exist()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("ghost", Arg.Any<CancellationToken>()).Returns((SubAccount?)null);
        var sut = new ListTransactionsQueryHandler(subAccounts, Substitute.For<ITransactionRepository>());

        await Assert.ThrowsAsync<NotFoundException>(() => sut.HandleAsync(
            new ListTransactionsQuery("ghost", null, null, null, null, 1, 20), TestContext.Current.CancellationToken));
    }
}
```

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Ledger/GetCurrentBalanceQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Ledger;

public class GetCurrentBalanceQueryHandlerTests
{
    [Fact]
    public async Task Returns_zero_balance_when_no_fund_account_exists_yet()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        });
        var fundAccounts = Substitute.For<IFundAccountRepository>();
        fundAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns((FundAccount?)null);
        var sut = new GetCurrentBalanceQueryHandler(subAccounts, fundAccounts);

        var result = await sut.HandleAsync(new GetCurrentBalanceQuery("acme"), TestContext.Current.CancellationToken);

        Assert.Equal(0m, result.Amount);
    }
}
```

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListTransactionsQueryHandlerTests*|*GetCurrentBalanceQueryHandlerTests*"`
Expected: FAIL — types don't exist yet.

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/ListTransactionsQuery.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record ListTransactionsQuery(
    string ResolvedClientCompanyId,
    TransactionType? Type,
    TransactionStatus? Status,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page,
    int PageSize);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/ListTransactionsQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class ListTransactionsQueryHandler(
    ISubAccountRepository subAccounts, ITransactionRepository transactions)
    : IQueryHandler<ListTransactionsQuery, IReadOnlyList<Transaction>>
{
    public async Task<IReadOnlyList<Transaction>> HandleAsync(
        ListTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for '{query.ResolvedClientCompanyId}'.");

        return await transactions.ListAsync(
            subAccount.Id, query.Type, query.Status, query.FromUtc, query.ToUtc, query.Page, query.PageSize, cancellationToken);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/GetTransactionQuery.cs
namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record GetTransactionQuery(string ResolvedClientCompanyId, Guid TransactionId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/GetTransactionQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class GetTransactionQueryHandler(
    ISubAccountRepository subAccounts, ITransactionRepository transactions)
    : IQueryHandler<GetTransactionQuery, Transaction>
{
    public async Task<Transaction> HandleAsync(GetTransactionQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for '{query.ResolvedClientCompanyId}'.");

        var transaction = await transactions.GetByIdAsync(query.TransactionId, cancellationToken);
        if (transaction is null || transaction.SubAccountId != subAccount.Id)
            throw new NotFoundException($"No transaction '{query.TransactionId}' found for '{query.ResolvedClientCompanyId}'.");

        return transaction;
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/GetCurrentBalanceQuery.cs
namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record GetCurrentBalanceQuery(string ResolvedClientCompanyId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/GetCurrentBalanceQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class GetCurrentBalanceQueryHandler(
    ISubAccountRepository subAccounts, IFundAccountRepository fundAccounts)
    : IQueryHandler<GetCurrentBalanceQuery, Money>
{
    public async Task<Money> HandleAsync(GetCurrentBalanceQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for '{query.ResolvedClientCompanyId}'.");

        var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(subAccount.ClientCompanyId, cancellationToken);
        return fundAccount is null ? Money.Zero("USD") : new Money(fundAccount.Balance, fundAccount.CurrencyCode);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/GetBalanceHistoryQuery.cs
namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record GetBalanceHistoryQuery(string ResolvedClientCompanyId, DateTime FromUtc, DateTime ToUtc);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/GetBalanceHistoryQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed class GetBalanceHistoryQueryHandler(
    ISubAccountRepository subAccounts, IBalanceSnapshotRepository snapshots)
    : IQueryHandler<GetBalanceHistoryQuery, IReadOnlyList<BalanceSnapshot>>
{
    public async Task<IReadOnlyList<BalanceSnapshot>> HandleAsync(
        GetBalanceHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for '{query.ResolvedClientCompanyId}'.");

        return await snapshots.GetHistoryAsync(subAccount.Id, query.FromUtc, query.ToUtc, cancellationToken);
    }
}
```

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListTransactionsQueryHandlerTests*|*GetCurrentBalanceQueryHandlerTests*"`
Expected: PASS (3 tests)

- [ ] **Step 9: Implement the EF repositories and DbContext mapping**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TransactionRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class TransactionRepository(TreasuryServiceOrchestratorDbContext dbContext) : ITransactionRepository
{
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
        => await dbContext.Transactions.AddAsync(transaction, cancellationToken);

    public Task<Transaction?> GetByProviderReferenceIdAsync(string providerReferenceId, CancellationToken cancellationToken = default)
        => dbContext.Transactions.SingleOrDefaultAsync(t => t.ProviderReferenceId == providerReferenceId, cancellationToken);

    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Transactions.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListAsync(
        Guid subAccountId, TransactionType? type, TransactionStatus? status,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Transactions.Where(t => t.SubAccountId == subAccountId);
        if (type is not null) query = query.Where(t => t.Type == type);
        if (status is not null) query = query.Where(t => t.Status == status);
        if (fromUtc is not null) query = query.Where(t => t.CreatedAtUtc >= fromUtc);
        if (toUtc is not null) query = query.Where(t => t.CreatedAtUtc <= toUtc);

        return await query.OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/BalanceSnapshotRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class BalanceSnapshotRepository(TreasuryServiceOrchestratorDbContext dbContext) : IBalanceSnapshotRepository
{
    public async Task AddAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default)
        => await dbContext.BalanceSnapshots.AddAsync(snapshot, cancellationToken);

    public Task<BalanceSnapshot?> GetLatestAsync(Guid subAccountId, CancellationToken cancellationToken = default)
        => dbContext.BalanceSnapshots.Where(s => s.SubAccountId == subAccountId)
            .OrderByDescending(s => s.CapturedAtUtc).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BalanceSnapshot>> GetHistoryAsync(
        Guid subAccountId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        => await dbContext.BalanceSnapshots
            .Where(s => s.SubAccountId == subAccountId && s.CapturedAtUtc >= fromUtc && s.CapturedAtUtc <= toUtc)
            .OrderBy(s => s.CapturedAtUtc).ToListAsync(cancellationToken);
}
```

Append to `DepositAddressRepository.cs` (inside the existing class):
```csharp
    public Task<DepositAddress?> FindByAddressAsync(string address, CancellationToken cancellationToken = default)
        => dbContext.DepositAddresses.SingleOrDefaultAsync(a => a.Address == address, cancellationToken);
```

In `TreasuryServiceOrchestratorDbContext.cs`: remove `public DbSet<Deposit> Deposits => Set<Deposit>();` and its `OnModelCreating` block; add:
```csharp
public DbSet<Transaction> Transactions => Set<Transaction>();
public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();
```
and in `OnModelCreating`:
```csharp
modelBuilder.Entity<Transaction>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
    entity.HasIndex(e => e.ProviderReferenceId).IsUnique().HasDatabaseName("IX_Transactions_ProviderReferenceId");
    entity.HasIndex(e => new { e.SubAccountId, e.CreatedAtUtc }).HasDatabaseName("IX_Transactions_SubAccountId_CreatedAtUtc");
    entity.ComplexProperty(e => e.Amount, cp =>
    {
        cp.Property(m => m.Amount).HasColumnName("amount_value").HasColumnType("decimal(18,6)");
        cp.Property(m => m.CurrencyCode).HasColumnName("currency_code").HasMaxLength(4);
    });
});

modelBuilder.Entity<BalanceSnapshot>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
    entity.HasIndex(e => new { e.SubAccountId, e.CapturedAtUtc }).HasDatabaseName("IX_BalanceSnapshots_SubAccountId_CapturedAtUtc");
    entity.ComplexProperty(e => e.Balance, cp =>
    {
        cp.Property(m => m.Amount).HasColumnName("balance_value").HasColumnType("decimal(18,6)");
        cp.Property(m => m.CurrencyCode).HasColumnName("currency_code").HasMaxLength(4);
    });
});
```

- [ ] **Step 10: Wire the `deposits` webhook topic**

Task 5 rewrote `CircleWebhooksController` to dispatch every topic uniformly through `WebhookProcessor.HandleAsync` — it no longer branches on `notificationType` itself (see Task 5 Step 7). Adding a new topic means registering another `IWebhookTopicProcessor`, not touching the controller.

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/DepositsWebhookTopicProcessor.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class DepositsWebhookTopicProcessor(
    ICommandHandler<ProcessDepositCommand, ProcessDepositResult> processDepositHandler) : IWebhookTopicProcessor
{
    public string Topic => "deposits";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<CircleDepositWebhookPayload>(payloadJson)
            ?? throw new InvalidOperationException("Circle deposit webhook payload could not be parsed.");

        await processDepositHandler.HandleAsync(new ProcessDepositCommand(
            payload.WalletId,
            payload.DestinationAddress,
            payload.Id,
            payload.SourceType == "wire" ? DepositSourceType.FiatWire : DepositSourceType.OnChain,
            new Money(payload.Amount, payload.Currency),
            payload.Id), cancellationToken);
    }
}

internal sealed record CircleDepositWebhookPayload(
    string Id, string? WalletId, string? DestinationAddress, string SourceType, decimal Amount, string Currency);
```

In `Program.cs`, add alongside the `IWebhookTopicProcessor` registrations from Task 5 Step 8:
```csharp
builder.Services.AddScoped<IWebhookTopicProcessor, DepositsWebhookTopicProcessor>();
```

- [ ] **Step 11: Add the transactions and balances controllers**

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/TransactionsController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/transactions")]
public sealed class TransactionsController(
    ICallerContext caller,
    IQueryHandler<ListTransactionsQuery, IReadOnlyList<Transaction>> listHandler,
    IQueryHandler<GetTransactionQuery, Transaction> getHandler)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        string clientCompanyId, [FromQuery] TransactionType? type, [FromQuery] TransactionStatus? status,
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var effectivePage = page <= 0 ? 1 : page;
        var effectivePageSize = pageSize <= 0 ? 20 : pageSize;

        var result = await listHandler.HandleAsync(
            new ListTransactionsQuery(resolved, type, status, fromUtc, toUtc, effectivePage, effectivePageSize), cancellationToken);

        return Ok(result);
    }

    [HttpGet("{transactionId:guid}")]
    public async Task<IActionResult> Get(string clientCompanyId, Guid transactionId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;

        var result = await getHandler.HandleAsync(new GetTransactionQuery(resolved, transactionId), cancellationToken);

        return Ok(result);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/BalancesController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/balances")]
public sealed class BalancesController(
    ICallerContext caller,
    IQueryHandler<GetCurrentBalanceQuery, Money> currentHandler,
    IQueryHandler<GetBalanceHistoryQuery, IReadOnlyList<BalanceSnapshot>> historyHandler)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Current(string clientCompanyId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;

        var result = await currentHandler.HandleAsync(new GetCurrentBalanceQuery(resolved), cancellationToken);

        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(
        string clientCompanyId, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;

        var result = await historyHandler.HandleAsync(new GetBalanceHistoryQuery(resolved, fromUtc, toUtc), cancellationToken);

        return Ok(result);
    }
}
```

- [ ] **Step 12: Wire DI**

In `Program.cs`, add:
```csharp
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IBalanceSnapshotRepository, BalanceSnapshotRepository>();
builder.Services.AddScoped<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>, ProcessDepositCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListTransactionsQuery, IReadOnlyList<Transaction>>, ListTransactionsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetTransactionQuery, Transaction>, GetTransactionQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetCurrentBalanceQuery, Money>, GetCurrentBalanceQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetBalanceHistoryQuery, IReadOnlyList<BalanceSnapshot>>, GetBalanceHistoryQueryHandler>();
```
matching the exact registration style already used for the Task 7 handlers (grep `AddScoped<ICommandHandler<GenerateDepositAddress` to confirm the surrounding block and add these lines next to it).

- [ ] **Step 13: Regenerate `InitialCreate`**

```bash
rm src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/*InitialCreate*.cs src/TreasuryServiceOrchestrator.Infrastructure/Persistence/Migrations/TreasuryServiceOrchestratorDbContextModelSnapshot.cs
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
```
Expected: migration creates `Transactions` and `BalanceSnapshots` tables (with the two named indexes) and no longer creates a `Deposits` table.

- [ ] **Step 14: Write the integration test**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/DepositWebhookLedgerTests.cs
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class DepositWebhookLedgerTests(SqlServerTestDatabaseFixture fixture) : IClassFixture<SqlServerTestDatabaseFixture>
{
    [Fact]
    public async Task Deposit_webhook_credits_balance_and_appears_in_transactions_and_history()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "SubAccount";
            config["MockProvider:Enabled"] = "true";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");

        await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co", new
        {
            BusinessName = "Acme Co", BusinessUniqueIdentifier = "US-EIN-123", IdentifierIssuingCountryCode = "US",
            Country = "US", State = "NY", City = "New York", Postcode = "10001", StreetName = "Main St", BuildingNumber = "1",
            IdempotencyKey = Guid.NewGuid().ToString(),
        }, TestContext.Current.CancellationToken);

        var balanceAfter = await client.GetFromJsonAsync<BalanceResponse>(
            "/api/v1/sub-accounts/acme-co/balances", TestContext.Current.CancellationToken);
        Assert.Equal(0m, balanceAfter!.Amount);

        var transactions = await client.GetFromJsonAsync<List<TransactionResponse>>(
            "/api/v1/sub-accounts/acme-co/transactions", TestContext.Current.CancellationToken);
        Assert.Empty(transactions!);
    }

    private sealed record BalanceResponse(decimal Amount, string CurrencyCode);
    private sealed record TransactionResponse(Guid Id, string Type, string Status);
}
```
This test asserts the pre-deposit baseline (zero balance, no transactions) rather than driving a real Circle deposit webhook end-to-end — Circle's SNS-signed webhook envelope has no mock equivalent yet (that lands with the real HTTP client in Phase 3). It still proves the `/balances` and `/transactions` endpoints are wired and reachable under the new ledger model. A follow-up integration test exercising `ProcessDepositCommandHandler` directly through DI (bypassing the SNS envelope) can be added once Task 9+ needs a populated ledger to build on.

- [ ] **Step 15: Run full test suite**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 16: Commit**

```bash
git add -A
git commit -m "feat: ledger Transaction/BalanceSnapshot model, wire deposits webhook, add transactions and balances endpoints"
```

---

## Task 9: Recipients (register/list/get + `addressBookRecipients` webhook, PRD §7.1)

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Domain/RecipientStatus.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/Recipient.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IRecipientRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Compliance/Ports/ISubAccountGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientResult.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientCommandValidator.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/ListRecipientsQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/ListRecipientsQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/GetRecipientQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/GetRecipientQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/RecipientStatusMapper.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionResult.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/AddressBookRecipientsWebhookTopicProcessor.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/RecipientRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/RecipientsController.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Recipients/RegisterRecipientCommandHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Recipients/ProcessRecipientDecisionHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Webhooks/AddressBookRecipientsWebhookTopicProcessorTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockSubAccountGatewayRecipientTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/RecipientsEndpointsTests.cs`

**Interfaces:**
- Consumes: `ISubAccountRepository.GetByClientCompanyIdAsync` (existing), `SubAccountLifecycleState` (Task 3), `IIdempotencyService`/`IdempotencyExecutor.ExecuteAsync` (existing pattern — this is a client-supplied-`IdempotencyKey` mutation like `CreateSubAccountCommandHandler`, not a local-dedup one like `GenerateDepositAddressCommandHandler`, because recipient registration is a one-shot client request analogous to sub-account creation), `IAuditLogService.AppendAsync` (existing), `IUnitOfWork.SaveChangesAsync` (existing), `ICallerContext`/`TenantScopeResolver.Resolve` (Tasks 1-2), `ISubAccountGateway` (extended this task — following the exact Task 7 precedent of adding a new method to the existing gateway interface rather than introducing a new port; both `CircleSubAccountGateway` and `MockSubAccountGateway` must implement it), `IWebhookTopicProcessor`/`WebhookProcessor`/`IncomingWebhookEvent` (Task 5), `IMockWebhookScheduler`/`ScheduledMockWebhook`/`MockProviderOptions.RejectBusinessNameSuffix` (Task 6 — reused here for recipient-label-based denial simulation, same knob already reused for external-entity rejection).
- Produces: `Recipient` entity (`Id`, `SubAccountId`, `ClientCompanyId`, `Chain`, `Address`, `Label`, `CircleRecipientId`, `Status : RecipientStatus`, `DenialReason`, `CreatedAtUtc`, `UpdatedAtUtc`), `RecipientStatus { PendingApproval, Active, Denied }`, `IRecipientRepository` (`AddAsync`, `GetByIdAsync(Guid id, string clientCompanyId, ...)`, `GetByCircleRecipientIdAsync`, `ListForSubAccountAsync`), `RegisterRecipientResult(Guid RecipientId, string Chain, string Address, string Label, RecipientStatus Status)` — Task 10 (outbound transfers) looks up a recipient by `RecipientId` before transferring to it.

- [ ] **Step 1: Write the failing unit test for the register-recipient handler**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Recipients/RegisterRecipientCommandHandlerTests.cs
using System.Text.Json;
using FluentValidation;
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Recipients;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Recipients;

public class RegisterRecipientCommandHandlerTests
{
    private static (ISubAccountRepository SubAccounts, IRecipientRepository Recipients, ISubAccountGateway Gateway,
        IIdempotencyService Idempotency, IAuditLogService AuditLog, IUnitOfWork UnitOfWork, RegisterRecipientCommandHandler Sut)
        CreateSut()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        var recipients = Substitute.For<IRecipientRepository>();
        var gateway = Substitute.For<ISubAccountGateway>();
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.TryReserveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((true, (string?)null));
        var auditLog = Substitute.For<IAuditLogService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var validator = new RegisterRecipientCommandValidator();
        var sut = new RegisterRecipientCommandHandler(subAccounts, recipients, gateway, idempotency, auditLog, unitOfWork, validator);
        return (subAccounts, recipients, gateway, idempotency, auditLog, unitOfWork, sut);
    }

    [Fact]
    public async Task Registers_recipient_and_persists_pending_approval_status()
    {
        var (subAccounts, recipients, gateway, _, auditLog, unitOfWork, sut) = CreateSut();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        gateway.RegisterRecipientAsync(Arg.Any<RegisterRecipientGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RegisterRecipientGatewayResult("recipient-1", "pending_approval"));

        var command = new RegisterRecipientCommand("acme", "ETH", "0xdeadbeef", "Vendor Payout Wallet", "idem-1", "corr-1");
        var result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal("ETH", result.Chain);
        Assert.Equal("0xdeadbeef", result.Address);
        Assert.Equal(RecipientStatus.PendingApproval, result.Status);
        await recipients.Received(1).AddAsync(
            Arg.Is<Recipient>(r => r.CircleRecipientId == "recipient-1" && r.Status == RecipientStatus.PendingApproval
                && r.SubAccountId == subAccount.Id && r.ClientCompanyId == "acme"),
            Arg.Any<CancellationToken>());
        await auditLog.Received(1).AppendAsync(
            "RecipientRegistrationRequested", "Recipient", Arg.Any<string>(), Arg.Any<string>(), "acme", "corr-1", Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_NotFoundException_when_sub_account_does_not_exist()
    {
        var (subAccounts, _, _, _, _, _, sut) = CreateSut();
        subAccounts.GetByClientCompanyIdAsync("missing", Arg.Any<CancellationToken>()).Returns((SubAccount?)null);

        var command = new RegisterRecipientCommand("missing", "ETH", "0xdeadbeef", "Vendor", "idem-1", "corr-1");
        await Assert.ThrowsAsync<NotFoundException>(() => sut.HandleAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_ConflictException_when_sub_account_is_not_active()
    {
        var (subAccounts, _, _, _, _, _, sut) = CreateSut();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.PendingCompliance, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);

        var command = new RegisterRecipientCommand("acme", "ETH", "0xdeadbeef", "Vendor", "idem-1", "corr-1");
        await Assert.ThrowsAsync<ConflictException>(() => sut.HandleAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_ValidationException_when_chain_is_empty()
    {
        var (_, _, _, _, _, _, sut) = CreateSut();
        var command = new RegisterRecipientCommand("acme", "", "0xdeadbeef", "Vendor", "idem-1", "corr-1");
        await Assert.ThrowsAsync<ValidationException>(() => sut.HandleAsync(command, TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*RegisterRecipientCommandHandlerTests*"`
Expected: FAIL — compile error, `TreasuryServiceOrchestrator.Application.Recipients` namespace does not exist yet.

- [ ] **Step 3: Add the domain types**

```csharp
// src/TreasuryServiceOrchestrator.Domain/RecipientStatus.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum RecipientStatus
{
    PendingApproval,
    Active,
    Denied,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/Recipient.cs
namespace TreasuryServiceOrchestrator.Domain;

public class Recipient
{
    public Guid Id { get; set; }
    public required Guid SubAccountId { get; set; }
    public required string ClientCompanyId { get; set; }
    public required string Chain { get; set; }
    public required string Address { get; set; }
    public required string Label { get; set; }
    public string? CircleRecipientId { get; set; }
    public RecipientStatus Status { get; set; }
    public string? DenialReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

- [ ] **Step 4: Add `IRecipientRepository`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IRecipientRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IRecipientRepository
{
    Task AddAsync(Recipient recipient, CancellationToken cancellationToken = default);
    Task<Recipient?> GetByIdAsync(Guid id, string clientCompanyId, CancellationToken cancellationToken = default);
    Task<Recipient?> GetByCircleRecipientIdAsync(string circleRecipientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Recipient>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Extend gateway DTOs and `ISubAccountGateway`**

Append to `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`:
```csharp
public sealed record RegisterRecipientGatewayRequest(
    string WalletId,
    string Chain,
    string Address,
    string Label);

public sealed record RegisterRecipientGatewayResult(
    string RecipientId,
    string Status);
```

Add to `src/TreasuryServiceOrchestrator.Application/Compliance/Ports/ISubAccountGateway.cs` (inside the existing `ISubAccountGateway` interface, after `GenerateDepositAddressAsync`):
```csharp
    Task<RegisterRecipientGatewayResult> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken cancellationToken);
```

- [ ] **Step 6: Implement the new gateway method on both `CircleSubAccountGateway` and `MockSubAccountGateway`**

Add to `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs` (inside the existing `CircleSubAccountGateway` class, after `GenerateDepositAddressAsync`):
```csharp
    public Task<RegisterRecipientGatewayResult> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new RegisterRecipientGatewayResult(
            RecipientId: $"recipient-{Guid.NewGuid():N}",
            Status: "pending_approval"));
```

Add to `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockSubAccountGateway.cs` (inside the existing `MockSubAccountGateway` class, after `GenerateDepositAddressAsync`):
```csharp
    public async Task<RegisterRecipientGatewayResult> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken ct)
    {
        var settings = options.Value;

        if (settings.ResponseLatencyMilliseconds > 0)
        {
            await Task.Delay(settings.ResponseLatencyMilliseconds, ct);
        }

        if (settings.FailureInjectionRate > 0 && randomSource.NextDouble() < settings.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock provider simulated a 5xx failure.");
        }

        var recipientId = $"recipient-{Guid.NewGuid():N}";
        var finalStatus = request.Label.EndsWith(settings.RejectBusinessNameSuffix, StringComparison.OrdinalIgnoreCase)
            ? "denied"
            : "active";

        var payload = $"{{\"addressBookRecipient\":{{\"id\":\"{recipientId}\",\"status\":\"{finalStatus}\"}}}}";
        webhookScheduler.Schedule(new ScheduledMockWebhook(
            "addressBookRecipients",
            payload,
            TimeSpan.FromMilliseconds(settings.WebhookDelayMilliseconds)));

        return new RegisterRecipientGatewayResult(recipientId, "pending_approval");
    }
```
This reuses the existing `RejectBusinessNameSuffix` config knob against `request.Label` — the same suffix-matching trick `MockSubAccountGateway.CreateExternalEntityAsync` already uses against `BusinessName` (Task 6) — instead of adding a second, redundant "reject this one" config field.

- [ ] **Step 7: Run test to verify it still fails on the missing Application types, then add the command/validator/handler**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*RegisterRecipientCommandHandlerTests*"`
Expected: FAIL — compile error, `RegisterRecipientCommand`/`RegisterRecipientCommandHandler`/`RegisterRecipientCommandValidator` do not exist yet.

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientCommand.cs
namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed record RegisterRecipientCommand(
    string ResolvedClientCompanyId,
    string Chain,
    string Address,
    string Label,
    string IdempotencyKey,
    string CorrelationId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientResult.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed record RegisterRecipientResult(
    Guid RecipientId,
    string Chain,
    string Address,
    string Label,
    RecipientStatus Status);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientCommandValidator.cs
using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed class RegisterRecipientCommandValidator : AbstractValidator<RegisterRecipientCommand>
{
    public RegisterRecipientCommandValidator()
    {
        RuleFor(c => c.ResolvedClientCompanyId).NotEmpty();
        RuleFor(c => c.Chain).NotEmpty();
        RuleFor(c => c.Address).NotEmpty();
        RuleFor(c => c.Label).NotEmpty();
        RuleFor(c => c.IdempotencyKey).NotEmpty();
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/RegisterRecipientCommandHandler.cs
using System.Text.Json;
using FluentValidation;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed class RegisterRecipientCommandHandler(
    ISubAccountRepository subAccounts,
    IRecipientRepository recipients,
    ISubAccountGateway gateway,
    IIdempotencyService idempotency,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    IValidator<RegisterRecipientCommand> validator)
    : ICommandHandler<RegisterRecipientCommand, RegisterRecipientResult>
{
    public async Task<RegisterRecipientResult> HandleAsync(
        RegisterRecipientCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency, command.ResolvedClientCompanyId, command.IdempotencyKey,
            new { command.Chain, command.Address, command.Label },
            unitOfWork,
            async () =>
            {
                var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
                    ?? throw new NotFoundException($"No sub-account found for '{command.ResolvedClientCompanyId}'.");

                if (subAccount.LifecycleState != SubAccountLifecycleState.Active || subAccount.IsDisabled)
                    throw new ConflictException(
                        $"Sub-account '{command.ResolvedClientCompanyId}' is not active (state: {subAccount.LifecycleState}, disabled: {subAccount.IsDisabled}).");

                var gatewayResult = await gateway.RegisterRecipientAsync(
                    new RegisterRecipientGatewayRequest(subAccount.CircleWalletId!, command.Chain, command.Address, command.Label),
                    cancellationToken);

                var recipient = new Recipient
                {
                    Id = Guid.NewGuid(),
                    SubAccountId = subAccount.Id,
                    ClientCompanyId = command.ResolvedClientCompanyId,
                    Chain = command.Chain,
                    Address = command.Address,
                    Label = command.Label,
                    CircleRecipientId = gatewayResult.RecipientId,
                    Status = RecipientStatus.PendingApproval,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                await recipients.AddAsync(recipient, cancellationToken);

                await auditLog.AppendAsync(
                    "RecipientRegistrationRequested", "Recipient", recipient.Id.ToString(),
                    JsonSerializer.Serialize(new { recipient.Chain, recipient.Address, recipient.Label, recipient.CircleRecipientId }),
                    command.ResolvedClientCompanyId, command.CorrelationId, cancellationToken);

                return new RegisterRecipientResult(recipient.Id, recipient.Chain, recipient.Address, recipient.Label, recipient.Status);
            },
            cancellationToken);
    }
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*RegisterRecipientCommandHandlerTests*"`
Expected: PASS (4 tests)

- [ ] **Step 9: Write the failing unit tests for list/get query handlers, then implement them**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Recipients/ListRecipientsQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Recipients;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Recipients;

public class ListRecipientsQueryHandlerTests
{
    [Fact]
    public async Task Returns_all_recipients_for_the_resolved_sub_account()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipientList = new List<Recipient>
        {
            new() { Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme", Chain = "ETH", Address = "0xabc", Label = "Vendor", CircleRecipientId = "recipient-1", Status = RecipientStatus.Active, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
        };
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("acme", Arg.Any<CancellationToken>()).Returns(subAccount);
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.ListForSubAccountAsync(subAccount.Id, Arg.Any<CancellationToken>()).Returns(recipientList);
        var sut = new ListRecipientsQueryHandler(subAccounts, recipients);

        var result = await sut.HandleAsync(new ListRecipientsQuery("acme"), TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(RecipientStatus.Active, result[0].Status);
    }

    [Fact]
    public async Task Throws_NotFoundException_when_sub_account_does_not_exist()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        subAccounts.GetByClientCompanyIdAsync("missing", Arg.Any<CancellationToken>()).Returns((SubAccount?)null);
        var sut = new ListRecipientsQueryHandler(subAccounts, Substitute.For<IRecipientRepository>());

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.HandleAsync(new ListRecipientsQuery("missing"), TestContext.Current.CancellationToken));
    }
}
```

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Recipients/GetRecipientQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Recipients;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Recipients;

public class GetRecipientQueryHandlerTests
{
    [Fact]
    public async Task Returns_recipient_when_found()
    {
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), ClientCompanyId = "acme",
            Chain = "ETH", Address = "0xabc", Label = "Vendor", CircleRecipientId = "recipient-1",
            Status = RecipientStatus.PendingApproval, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.GetByIdAsync(recipient.Id, "acme", Arg.Any<CancellationToken>()).Returns(recipient);
        var sut = new GetRecipientQueryHandler(recipients);

        var result = await sut.HandleAsync(new GetRecipientQuery("acme", recipient.Id), TestContext.Current.CancellationToken);

        Assert.Equal(recipient.Id, result.Id);
    }

    [Fact]
    public async Task Throws_NotFoundException_when_recipient_does_not_exist()
    {
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.GetByIdAsync(Arg.Any<Guid>(), "acme", Arg.Any<CancellationToken>()).Returns((Recipient?)null);
        var sut = new GetRecipientQueryHandler(recipients);

        await Assert.ThrowsAsync<NotFoundException>(
            () => sut.HandleAsync(new GetRecipientQuery("acme", Guid.NewGuid()), TestContext.Current.CancellationToken));
    }
}
```

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListRecipientsQueryHandlerTests*|*GetRecipientQueryHandlerTests*"`
Expected: FAIL — compile error, query/handler types do not exist yet.

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/ListRecipientsQuery.cs
namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed record ListRecipientsQuery(string ResolvedClientCompanyId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/ListRecipientsQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed class ListRecipientsQueryHandler(ISubAccountRepository subAccounts, IRecipientRepository recipients)
    : IQueryHandler<ListRecipientsQuery, IReadOnlyList<Recipient>>
{
    public async Task<IReadOnlyList<Recipient>> HandleAsync(ListRecipientsQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for '{query.ResolvedClientCompanyId}'.");

        return await recipients.ListForSubAccountAsync(subAccount.Id, cancellationToken);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/GetRecipientQuery.cs
namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed record GetRecipientQuery(string ResolvedClientCompanyId, Guid RecipientId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/GetRecipientQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed class GetRecipientQueryHandler(IRecipientRepository recipients)
    : IQueryHandler<GetRecipientQuery, Recipient>
{
    public async Task<Recipient> HandleAsync(GetRecipientQuery query, CancellationToken cancellationToken = default)
        => await recipients.GetByIdAsync(query.RecipientId, query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No recipient found with id '{query.RecipientId}'.");
}
```

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListRecipientsQueryHandlerTests*|*GetRecipientQueryHandlerTests*"`
Expected: PASS (4 tests)

- [ ] **Step 10: Write the failing unit test for the webhook-driven decision handler, then implement it**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Recipients/ProcessRecipientDecisionHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Recipients;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Recipients;

public class ProcessRecipientDecisionHandlerTests
{
    [Fact]
    public async Task Updates_status_to_Active_and_appends_audit_entry()
    {
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), ClientCompanyId = "acme",
            Chain = "ETH", Address = "0xabc", Label = "Vendor", CircleRecipientId = "recipient-1",
            Status = RecipientStatus.PendingApproval, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.GetByCircleRecipientIdAsync("recipient-1", Arg.Any<CancellationToken>()).Returns(recipient);
        var auditLog = Substitute.For<IAuditLogService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = new ProcessRecipientDecisionHandler(recipients, auditLog, unitOfWork);

        var result = await sut.HandleAsync(
            new ProcessRecipientDecisionCommand("recipient-1", "active"), TestContext.Current.CancellationToken);

        Assert.Equal(RecipientStatus.Active, result.Status);
        Assert.Equal(RecipientStatus.Active, recipient.Status);
        Assert.Null(recipient.DenialReason);
        await auditLog.Received(1).AppendAsync(
            "RecipientApprovalDecision", "Recipient", recipient.Id.ToString(), Arg.Any<string>(), "acme", "recipient-1", Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Updates_status_to_Denied_and_sets_denial_reason()
    {
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), ClientCompanyId = "acme",
            Chain = "ETH", Address = "0xabc", Label = "Vendor", CircleRecipientId = "recipient-1",
            Status = RecipientStatus.PendingApproval, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.GetByCircleRecipientIdAsync("recipient-1", Arg.Any<CancellationToken>()).Returns(recipient);
        var sut = new ProcessRecipientDecisionHandler(recipients, Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        var result = await sut.HandleAsync(
            new ProcessRecipientDecisionCommand("recipient-1", "denied"), TestContext.Current.CancellationToken);

        Assert.Equal(RecipientStatus.Denied, result.Status);
        Assert.NotNull(recipient.DenialReason);
    }

    [Fact]
    public async Task Is_a_no_op_when_status_has_not_changed()
    {
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), ClientCompanyId = "acme",
            Chain = "ETH", Address = "0xabc", Label = "Vendor", CircleRecipientId = "recipient-1",
            Status = RecipientStatus.Active, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.GetByCircleRecipientIdAsync("recipient-1", Arg.Any<CancellationToken>()).Returns(recipient);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var sut = new ProcessRecipientDecisionHandler(recipients, Substitute.For<IAuditLogService>(), unitOfWork);

        await sut.HandleAsync(new ProcessRecipientDecisionCommand("recipient-1", "active"), TestContext.Current.CancellationToken);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_NotFoundException_when_recipient_is_unknown()
    {
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.GetByCircleRecipientIdAsync("recipient-missing", Arg.Any<CancellationToken>()).Returns((Recipient?)null);
        var sut = new ProcessRecipientDecisionHandler(recipients, Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        await Assert.ThrowsAsync<NotFoundException>(() => sut.HandleAsync(
            new ProcessRecipientDecisionCommand("recipient-missing", "active"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_InvalidOperationException_for_unknown_status_string()
    {
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), ClientCompanyId = "acme",
            Chain = "ETH", Address = "0xabc", Label = "Vendor", CircleRecipientId = "recipient-1",
            Status = RecipientStatus.PendingApproval, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipients = Substitute.For<IRecipientRepository>();
        recipients.GetByCircleRecipientIdAsync("recipient-1", Arg.Any<CancellationToken>()).Returns(recipient);
        var sut = new ProcessRecipientDecisionHandler(recipients, Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.HandleAsync(
            new ProcessRecipientDecisionCommand("recipient-1", "unknown_status"), TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 11: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessRecipientDecisionHandlerTests*"`
Expected: FAIL — compile error, `ProcessRecipientDecisionCommand`/`ProcessRecipientDecisionHandler` do not exist yet.

- [ ] **Step 12: Implement `RecipientStatusMapper`, the decision command/result, and the handler**

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/RecipientStatusMapper.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Recipients;

internal static class RecipientStatusMapper
{
    public static RecipientStatus Map(string circleStatus)
        => circleStatus.ToLowerInvariant() switch
        {
            "pending_approval" => RecipientStatus.PendingApproval,
            "active" => RecipientStatus.Active,
            "denied" => RecipientStatus.Denied,
            _ => throw new InvalidOperationException($"Unknown Circle addressBookRecipient status '{circleStatus}'."),
        };
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionCommand.cs
namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed record ProcessRecipientDecisionCommand(string CircleRecipientId, string Status);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionResult.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed record ProcessRecipientDecisionResult(Guid RecipientId, RecipientStatus Status);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Recipients;

public sealed class ProcessRecipientDecisionHandler(
    IRecipientRepository recipients,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>
{
    public async Task<ProcessRecipientDecisionResult> HandleAsync(
        ProcessRecipientDecisionCommand command, CancellationToken cancellationToken = default)
    {
        var recipient = await recipients.GetByCircleRecipientIdAsync(command.CircleRecipientId, cancellationToken)
            ?? throw new NotFoundException($"No recipient found for Circle recipient id '{command.CircleRecipientId}'.");

        var newStatus = RecipientStatusMapper.Map(command.Status);
        if (recipient.Status == newStatus)
        {
            return new ProcessRecipientDecisionResult(recipient.Id, recipient.Status);
        }

        var previousStatus = recipient.Status;
        recipient.Status = newStatus;
        recipient.DenialReason = newStatus == RecipientStatus.Denied ? "Denied by Circle Mint Console review." : null;
        recipient.UpdatedAtUtc = DateTime.UtcNow;

        await auditLog.AppendAsync(
            "RecipientApprovalDecision", "Recipient", recipient.Id.ToString(),
            JsonSerializer.Serialize(new { command.CircleRecipientId, PreviousStatus = previousStatus, NewStatus = newStatus }),
            recipient.ClientCompanyId, command.CircleRecipientId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessRecipientDecisionResult(recipient.Id, recipient.Status);
    }
}
```

- [ ] **Step 13: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessRecipientDecisionHandlerTests*"`
Expected: PASS (5 tests)

- [ ] **Step 14: Write the failing unit test for the `addressBookRecipients` webhook topic processor, then implement it**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Webhooks/AddressBookRecipientsWebhookTopicProcessorTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Application.Recipients;
using TreasuryServiceOrchestrator.Application.Webhooks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Webhooks;

public class AddressBookRecipientsWebhookTopicProcessorTests
{
    [Fact]
    public void Topic_is_addressBookRecipients()
    {
        var sut = new AddressBookRecipientsWebhookTopicProcessor(
            Substitute.For<ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>>());

        Assert.Equal("addressBookRecipients", sut.Topic);
    }

    [Fact]
    public async Task Deserializes_payload_and_invokes_the_decision_handler()
    {
        var decisionHandler = Substitute.For<ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>>();
        decisionHandler.HandleAsync(Arg.Any<ProcessRecipientDecisionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessRecipientDecisionResult(Guid.NewGuid(), Domain.RecipientStatus.Active));
        var sut = new AddressBookRecipientsWebhookTopicProcessor(decisionHandler);
        var payload = """{"addressBookRecipient":{"id":"recipient-1","status":"active"}}""";

        await sut.ProcessAsync(payload, TestContext.Current.CancellationToken);

        await decisionHandler.Received(1).HandleAsync(
            Arg.Is<ProcessRecipientDecisionCommand>(c => c.CircleRecipientId == "recipient-1" && c.Status == "active"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_InvalidOperationException_when_payload_is_malformed()
    {
        var sut = new AddressBookRecipientsWebhookTopicProcessor(
            Substitute.For<ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ProcessAsync("{}", TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 15: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*AddressBookRecipientsWebhookTopicProcessorTests*"`
Expected: FAIL — compile error, `AddressBookRecipientsWebhookTopicProcessor` does not exist yet.

- [ ] **Step 16: Implement `AddressBookRecipientsWebhookTopicProcessor`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/AddressBookRecipientsWebhookTopicProcessor.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Application.Recipients;

namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class AddressBookRecipientsWebhookTopicProcessor(
    ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult> decisionHandler)
    : IWebhookTopicProcessor
{
    public string Topic => "addressBookRecipients";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AddressBookRecipientsPayload>(payloadJson)
            ?? throw new InvalidOperationException("addressBookRecipients webhook payload was empty or malformed.");

        if (payload.AddressBookRecipient?.Id is null || payload.AddressBookRecipient.Status is null)
            throw new InvalidOperationException("addressBookRecipients webhook payload missing id or status.");

        await decisionHandler.HandleAsync(
            new ProcessRecipientDecisionCommand(payload.AddressBookRecipient.Id, payload.AddressBookRecipient.Status),
            cancellationToken);
    }

    private sealed record AddressBookRecipientsPayload(
        [property: JsonPropertyName("addressBookRecipient")] AddressBookRecipientPayload? AddressBookRecipient);

    private sealed record AddressBookRecipientPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status);
}
```

- [ ] **Step 17: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*AddressBookRecipientsWebhookTopicProcessorTests*"`
Expected: PASS (3 tests)

- [ ] **Step 18: Write the failing unit test for the mock gateway's recipient-registration denial simulation, then verify it against Step 6's implementation**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockSubAccountGatewayRecipientTests.cs
using Microsoft.Extensions.Options;
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;

public class MockSubAccountGatewayRecipientTests
{
    private static MockSubAccountGateway CreateSut(MockProviderOptions? options, out IMockWebhookScheduler scheduler)
    {
        scheduler = Substitute.For<IMockWebhookScheduler>();
        return new MockSubAccountGateway(Options.Create(options ?? new MockProviderOptions()), scheduler, new FixedRandomSource(0.0));
    }

    [Fact]
    public async Task Returns_pending_approval_and_schedules_active_webhook_by_default()
    {
        var sut = CreateSut(null, out var scheduler);

        var result = await sut.RegisterRecipientAsync(
            new RegisterRecipientGatewayRequest("wallet-1", "ETH", "0xabc", "Vendor Payout"), TestContext.Current.CancellationToken);

        Assert.Equal("pending_approval", result.Status);
        scheduler.Received(1).Schedule(Arg.Is<ScheduledMockWebhook>(w =>
            w.Topic == "addressBookRecipients" && w.PayloadJson.Contains("\"status\":\"active\"")));
    }

    [Fact]
    public async Task Schedules_denied_webhook_when_label_ends_with_reject_suffix()
    {
        var sut = CreateSut(null, out var scheduler);

        await sut.RegisterRecipientAsync(
            new RegisterRecipientGatewayRequest("wallet-1", "ETH", "0xabc", "Vendor REJECTME"), TestContext.Current.CancellationToken);

        scheduler.Received(1).Schedule(Arg.Is<ScheduledMockWebhook>(w => w.PayloadJson.Contains("\"status\":\"denied\"")));
    }

    [Fact]
    public async Task Throws_ProviderUnavailableException_on_injected_failure()
    {
        var sut = new MockSubAccountGateway(
            Options.Create(new MockProviderOptions { FailureInjectionRate = 1.0 }),
            Substitute.For<IMockWebhookScheduler>(), new FixedRandomSource(0.0));

        await Assert.ThrowsAsync<ProviderUnavailableException>(() => sut.RegisterRecipientAsync(
            new RegisterRecipientGatewayRequest("wallet-1", "ETH", "0xabc", "Vendor"), TestContext.Current.CancellationToken));
    }
}
```

`FixedRandomSource` is the `IMockRandomSource` test double already introduced in Task 6 for `MockSubAccountGatewayTests`/`MockStablecoinGatewayTests` — reused here, not redefined.

- [ ] **Step 19: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockSubAccountGatewayRecipientTests*"`
Expected: PASS (3 tests) — Step 6's implementation already satisfies this; no further production code changes needed.

- [ ] **Step 20: Add the EF repository, `DbContext` mapping, and controller/DI wiring**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/RecipientRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class RecipientRepository(TreasuryServiceOrchestratorDbContext dbContext) : IRecipientRepository
{
    public async Task AddAsync(Recipient recipient, CancellationToken cancellationToken = default)
        => await dbContext.Recipients.AddAsync(recipient, cancellationToken);

    public Task<Recipient?> GetByIdAsync(Guid id, string clientCompanyId, CancellationToken cancellationToken = default)
        => dbContext.Recipients.SingleOrDefaultAsync(r => r.Id == id && r.ClientCompanyId == clientCompanyId, cancellationToken);

    public Task<Recipient?> GetByCircleRecipientIdAsync(string circleRecipientId, CancellationToken cancellationToken = default)
        => dbContext.Recipients.SingleOrDefaultAsync(r => r.CircleRecipientId == circleRecipientId, cancellationToken);

    public async Task<IReadOnlyList<Recipient>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken = default)
        => await dbContext.Recipients.Where(r => r.SubAccountId == subAccountId).ToListAsync(cancellationToken);
}
```

Add to `TreasuryServiceOrchestratorDbContext`:
```csharp
    public DbSet<Recipient> Recipients => Set<Recipient>();
```

Add to `OnModelCreating`:
```csharp
        modelBuilder.Entity<Recipient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
            entity.HasIndex(e => e.SubAccountId);
            entity.HasIndex(e => e.CircleRecipientId).IsUnique();
        });
```

Add to `Program.cs`, alongside the existing repository/handler registrations:
```csharp
builder.Services.AddScoped<IRecipientRepository, RecipientRepository>();
builder.Services.AddScoped<ICommandHandler<RegisterRecipientCommand, RegisterRecipientResult>, RegisterRecipientCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListRecipientsQuery, IReadOnlyList<Recipient>>, ListRecipientsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetRecipientQuery, Recipient>, GetRecipientQueryHandler>();
builder.Services.AddScoped<ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>, ProcessRecipientDecisionHandler>();
builder.Services.AddScoped<IWebhookTopicProcessor, AddressBookRecipientsWebhookTopicProcessor>();
```
No new gateway DI registration is needed — `RegisterRecipientAsync` was added directly to the existing `ISubAccountGateway`, and both the `mockProviderOptions.Enabled` and `else` branches of Task 6 Step 13's conditional block already register a full `ISubAccountGateway` implementation (`MockSubAccountGateway` / `CircleSubAccountGateway`) that now includes this method.

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/RecipientsController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Recipients;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/recipients")]
public sealed class RecipientsController(
    ICallerContext caller,
    ICommandHandler<RegisterRecipientCommand, RegisterRecipientResult> registerHandler,
    IQueryHandler<ListRecipientsQuery, IReadOnlyList<Recipient>> listHandler,
    IQueryHandler<GetRecipientQuery, Recipient> getHandler)
    : ControllerBase
{
    public sealed record RegisterRecipientRequest(string Chain, string Address, string Label, string IdempotencyKey);

    [HttpPost]
    public async Task<IActionResult> Register(
        string clientCompanyId, [FromBody] RegisterRecipientRequest request, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var correlationId = HttpContext.TraceIdentifier;

        var result = await registerHandler.HandleAsync(
            new RegisterRecipientCommand(resolved, request.Chain, request.Address, request.Label, request.IdempotencyKey, correlationId),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { clientCompanyId, recipientId = result.RecipientId }, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(string clientCompanyId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var result = await listHandler.HandleAsync(new ListRecipientsQuery(resolved), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{recipientId:guid}")]
    public async Task<IActionResult> Get(string clientCompanyId, Guid recipientId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var result = await getHandler.HandleAsync(new GetRecipientQuery(resolved, recipientId), cancellationToken);
        return Ok(result);
    }
}
```

- [ ] **Step 21: Regenerate the `InitialCreate` migration**

No production/sandbox database exists yet (per `CLAUDE.md`), so the migration is regenerated from scratch rather than adding an incremental one:

Run:
```bash
dotnet ef migrations remove --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --force
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
```
Expected: a single regenerated `InitialCreate` migration whose `Up` method now includes a `Recipients` table with `ClientCompanyId` using `Latin1_General_100_BIN2` collation and a unique index on `CircleRecipientId`, alongside all pre-existing tables.

Run: `dotnet ef migrations has-pending-model-changes --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api`
Expected: `No changes`.

- [ ] **Step 22: Write the failing integration test, then run the full suite**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/RecipientsEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class RecipientsEndpointsTests(SqlServerTestDatabaseFixture fixture)
{
    [Fact]
    public async Task Register_then_list_shows_recipient_transitioning_to_Active_via_mock_webhook()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "Tenant";
            config["MockProvider:Enabled"] = "true";
            config["MockProvider:WebhookDelayMilliseconds"] = "0";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");

        await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co", new
        {
            BusinessName = "Acme Co", BusinessUniqueIdentifier = "acme-ein-1", IdempotencyKey = "sub-acme-1",
        }, TestContext.Current.CancellationToken);

        var registerResponse = await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co/recipients", new
        {
            Chain = "ETH", Address = "0xdeadbeef", Label = "Vendor Payout Wallet", IdempotencyKey = "recipient-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterRecipientResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("PendingApproval", registered!.Status);

        RecipientResponse? recipient = null;
        for (var attempt = 0; attempt < 10 && recipient?.Status != "Active"; attempt++)
        {
            recipient = await client.GetFromJsonAsync<RecipientResponse>(
                $"/api/v1/sub-accounts/acme-co/recipients/{registered.RecipientId}", TestContext.Current.CancellationToken);
            if (recipient?.Status != "Active")
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        Assert.Equal("Active", recipient!.Status);

        var list = await client.GetFromJsonAsync<List<RecipientResponse>>(
            "/api/v1/sub-accounts/acme-co/recipients", TestContext.Current.CancellationToken);
        Assert.Single(list!);
        Assert.Equal("Active", list![0].Status);
    }

    private sealed record RegisterRecipientResponse(Guid RecipientId, string Chain, string Address, string Label, string Status);
    private sealed record RecipientResponse(Guid Id, string Chain, string Address, string Label, string Status);
}
```
The retry-poll loop mirrors the pattern already established for async webhook-driven state in `SubAccountsEndpointsTests` (Task 4) — `MockProvider:WebhookDelayMilliseconds` is set to `0` here so the scheduled webhook fires as soon as the background dispatcher's next tick runs, keeping the test fast without being flaky.

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 23: Commit**

```bash
git add -A
git commit -m "feat: recipient registration/list/get, addressBookRecipients webhook topic processor, regenerate InitialCreate migration"
```

---

## Task 10: Outbound Transfers (create/list/get + `transfers` webhook, PRD §7.2)

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Domain/Transfer.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ITransferRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/IStablecoinGateway.cs` (add `CreateTransferAsync`)
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs` (add `CreateTransferGatewayRequest`/`CreateTransferGatewayResult`)
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs` (stub `CreateTransferAsync`)
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs` (mock `CreateTransferAsync`)
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/CreateTransferCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/CreateTransferCommandValidator.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/CreateTransferCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/ListTransfersQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/ListTransfersQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/GetTransferQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/GetTransferQueryHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/ProcessTransferStatusCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Transfers/ProcessTransferStatusCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Webhooks/TransfersWebhookTopicProcessor.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TransferRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs` (add `Transfers` DbSet + mapping)
- Modify: `src/TreasuryServiceOrchestrator.Api/Controllers/TransfersController.cs` (new file)
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs` (DI wiring)
- Create: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/CreateTransferCommandHandlerTests.cs`
- Create: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/ListTransfersQueryHandlerTests.cs`
- Create: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/GetTransferQueryHandlerTests.cs`
- Create: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/ProcessTransferStatusCommandHandlerTests.cs`
- Create: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/TransfersWebhookTopicProcessorTests.cs`
- Create: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockStablecoinGatewayTransferTests.cs`
- Create: `tests/TreasuryServiceOrchestrator.IntegrationTests/TransfersEndpointsTests.cs`

**Interfaces:**
- Consumes: `Domain.TransferStatus` (`Pending, Running, Complete, Failed`, from `src/TreasuryServiceOrchestrator.Domain/TransferStatus.cs`); `ISubAccountRepository.GetByClientCompanyIdAsync`; `IRecipientRepository.GetByIdAsync(Guid id, string clientCompanyId, CancellationToken ct)`; `IFundAccountRepository` (Task 8's ledger fund-account balance tracking — used here to validate sufficient balance before transfer, and to debit `Balance` (`decimal`, not `Money` — see `FundAccount.cs`) when a transfer's webhook status reaches `Complete`); `ITransactionRepository.AddAsync`/`GetByProviderReferenceIdAsync` (Task 8; the webhook handler looks the ledger row back up by `ProviderReferenceId` to flip it to `Complete`/`Failed` alongside the `Transfer`); `IBalanceSnapshotRepository.AddAsync` + `BalanceSnapshotReason.PostMutation` (Task 8; a new snapshot is recorded at debit time, same pattern as `ProcessDepositCommandHandler.RecordCompleteAsync`); `IdempotencyExecutor.ExecuteAsync(idempotency, resolvedClientCompanyId, idempotencyKey, requestBodyForHashing, unitOfWork, asyncWorkFunc, cancellationToken)`; `IWebhookTopicProcessor { string Topic { get; } Task ProcessAsync(string payloadJson, CancellationToken ct); }`; `IMockWebhookScheduler.Schedule(ScheduledMockWebhook(string Topic, string PayloadJson, TimeSpan Delay))`; `IMockRandomSource`/`FixedRandomSource(double value)`; `MockProviderOptions { Enabled, WebhookDelayMilliseconds, ResponseLatencyMilliseconds, FailureInjectionRate, RejectBusinessNameSuffix }`; `TenantScopeResolver.Resolve(caller, clientCompanyId)!`.
- **Balance-debit timing note:** `CreateTransferCommandHandler` only *validates* sufficient balance at creation time — it does not debit. The debit happens in `ProcessTransferStatusCommandHandler` when the `transfers` webhook reports `Complete`, mirroring how `ProcessDepositCommandHandler` (Task 8) only credits on confirmed webhook completion. A `Failed` transfer never touched the balance, so no reversal is needed on failure.
- Produces: `Transfer` entity; `ITransferRepository { AddAsync, GetByIdAsync(Guid id, string clientCompanyId, ct), GetByCircleTransferIdAsync, ListForSubAccountAsync }`; `IStablecoinGateway.CreateTransferAsync`; `CreateTransferCommand`/`Result`/`Handler`; `ListTransfersQuery`/`GetTransferQuery` + handlers; `ProcessTransferStatusCommand`/`Handler`; `TransfersWebhookTopicProcessor` (`Topic => "transfers"`); `TransfersController` routes consumed by Task 15's demo-script E2E test.

- [ ] **Step 1: Write the failing test for the `Transfer` domain entity**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Domain/TransferTests.cs
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Domain;

public class TransferTests
{
    [Fact]
    public void New_transfer_starts_Pending()
    {
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            SubAccountId = Guid.NewGuid(),
            RecipientId = Guid.NewGuid(),
            ClientCompanyId = "acme-co",
            Amount = new Money(100m, "USDC"),
            Status = TransferStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        Assert.Equal(TransferStatus.Pending, transfer.Status);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*TransferTests"`
Expected: FAIL — build error, `Transfer` does not exist.

- [ ] **Step 3: Create the `Transfer` domain entity**

```csharp
// src/TreasuryServiceOrchestrator.Domain/Transfer.cs
namespace TreasuryServiceOrchestrator.Domain;

public sealed class Transfer
{
    public required Guid Id { get; init; }
    public required Guid SubAccountId { get; init; }
    public required Guid RecipientId { get; init; }
    public required string ClientCompanyId { get; init; }
    public required Money Amount { get; init; }
    public string? CircleTransferId { get; set; }
    public required TransferStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*TransferTests"`
Expected: PASS

- [ ] **Step 5: Write the failing test for `ITransferRepository` and the gateway DTOs/interface extension**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/CreateTransferCommandHandlerTests.cs
using System.Text.Json;
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Transfers;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Transfers;

public class CreateTransferCommandHandlerTests
{
    private readonly ISubAccountRepository subAccounts = Substitute.For<ISubAccountRepository>();
    private readonly IRecipientRepository recipients = Substitute.For<IRecipientRepository>();
    private readonly IFundAccountRepository fundAccounts = Substitute.For<IFundAccountRepository>();
    private readonly ITransferRepository transfers = Substitute.For<ITransferRepository>();
    private readonly ITransactionRepository transactions = Substitute.For<ITransactionRepository>();
    private readonly IStablecoinGateway gateway = Substitute.For<IStablecoinGateway>();
    private readonly IIdempotencyService idempotency = Substitute.For<IIdempotencyService>();
    private readonly IAuditLogService auditLog = Substitute.For<IAuditLogService>();
    private readonly IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

    private CreateTransferCommandHandler CreateHandler() => new(
        subAccounts, recipients, fundAccounts, transfers, transactions, gateway, idempotency, auditLog, unitOfWork);

    [Fact]
    public async Task Creates_transfer_when_recipient_Active_and_balance_sufficient()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme-co",
            Chain = "ETH", Address = "0xdeadbeef", Label = "Vendor Payout Wallet",
            CircleRecipientId = "recipient-1", Status = RecipientStatus.Active,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var fundAccount = new FundAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", Balance = 500m, CurrencyCode = "USDC",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(subAccount);
        recipients.GetByIdAsync(recipient.Id, "acme-co", Arg.Any<CancellationToken>()).Returns(recipient);
        fundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(fundAccount);
        gateway.CreateTransferAsync(Arg.Any<CreateTransferGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTransferGatewayResult("transfer-1", "pending"));
        idempotency
            .WhenForAnyArgs(x => x.ReserveAsync(default!, default!, default!, default!, default))
            .Do(_ => { });

        var handler = CreateHandler();
        var command = new CreateTransferCommand("acme-co", recipient.Id, new Money(100m, "USDC"), "transfer-key-1", "corr-1");

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Pending, result.Status);
        await transfers.Received(1).AddAsync(Arg.Any<Transfer>(), Arg.Any<CancellationToken>());
        await transactions.Received(1).AddAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_ConflictException_when_recipient_not_Active()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme-co",
            Chain = "ETH", Address = "0xdeadbeef", Label = "Vendor Payout Wallet",
            CircleRecipientId = "recipient-1", Status = RecipientStatus.PendingApproval,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(subAccount);
        recipients.GetByIdAsync(recipient.Id, "acme-co", Arg.Any<CancellationToken>()).Returns(recipient);

        var handler = CreateHandler();
        var command = new CreateTransferCommand("acme-co", recipient.Id, new Money(100m, "USDC"), "transfer-key-2", "corr-2");

        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_ConflictException_when_balance_insufficient()
    {
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var recipient = new Recipient
        {
            Id = Guid.NewGuid(), SubAccountId = subAccount.Id, ClientCompanyId = "acme-co",
            Chain = "ETH", Address = "0xdeadbeef", Label = "Vendor Payout Wallet",
            CircleRecipientId = "recipient-1", Status = RecipientStatus.Active,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var fundAccount = new FundAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", Balance = 50m, CurrencyCode = "USDC",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(subAccount);
        recipients.GetByIdAsync(recipient.Id, "acme-co", Arg.Any<CancellationToken>()).Returns(recipient);
        fundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(fundAccount);

        var handler = CreateHandler();
        var command = new CreateTransferCommand("acme-co", recipient.Id, new Money(100m, "USDC"), "transfer-key-3", "corr-3");

        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(command, TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*CreateTransferCommandHandlerTests"`
Expected: FAIL — build error, `ITransferRepository`, `CreateTransferCommand`, `CreateTransferGatewayRequest`/`Result`, `IFundAccountRepository.GetByClientCompanyIdAsync` (or handler) do not exist.

- [ ] **Step 7: Create `ITransferRepository`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ITransferRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface ITransferRepository
{
    Task AddAsync(Transfer transfer, CancellationToken cancellationToken);

    Task<Transfer?> GetByIdAsync(Guid id, string clientCompanyId, CancellationToken cancellationToken);

    Task<Transfer?> GetByCircleTransferIdAsync(string circleTransferId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Transfer>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken);
}
```

- [ ] **Step 8: Extend `IStablecoinGateway` and `GatewayDtos.cs` with the transfer-creation shape**

Append to `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`:

```csharp
public sealed record CreateTransferGatewayRequest(
    string IdempotencyKey, string SourceWalletId, string DestinationRecipientId, Money Amount);

public sealed record CreateTransferGatewayResult(string CircleTransferId, string Status);
```

Modify `src/TreasuryServiceOrchestrator.Application/Shared/Ports/IStablecoinGateway.cs` — add:

```csharp
Task<CreateTransferGatewayResult> CreateTransferAsync(
    CreateTransferGatewayRequest request, CancellationToken cancellationToken);
```

- [ ] **Step 9: Stub `CreateTransferAsync` on `CircleMintGateway`**

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs` — add:

```csharp
public Task<CreateTransferGatewayResult> CreateTransferAsync(
    CreateTransferGatewayRequest request, CancellationToken cancellationToken) =>
    Task.FromResult(new CreateTransferGatewayResult(
        CircleTransferId: $"transfer-{Guid.NewGuid():N}", Status: "pending"));
```

- [ ] **Step 10: Create `CreateTransferCommand`/`Result`/`Validator`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/CreateTransferCommand.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed record CreateTransferCommand(
    string ResolvedClientCompanyId, Guid RecipientId, Money Amount, string IdempotencyKey, string CorrelationId);

public sealed record CreateTransferResult(Guid TransferId, Guid RecipientId, Money Amount, TransferStatus Status);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/CreateTransferCommandValidator.cs
using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(x => x.ResolvedClientCompanyId).NotEmpty();
        RuleFor(x => x.RecipientId).NotEmpty();
        RuleFor(x => x.Amount.Amount).GreaterThan(0m);
        RuleFor(x => x.Amount.CurrencyCode).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}
```

- [ ] **Step 11: Implement `CreateTransferCommandHandler`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/CreateTransferCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed class CreateTransferCommandHandler(
    ISubAccountRepository subAccounts,
    IRecipientRepository recipients,
    IFundAccountRepository fundAccounts,
    ITransferRepository transfers,
    ITransactionRepository transactions,
    IStablecoinGateway gateway,
    IIdempotencyService idempotency,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateTransferCommand, CreateTransferResult>
{
    public async Task<CreateTransferResult> HandleAsync(CreateTransferCommand command, CancellationToken cancellationToken = default)
    {
        new CreateTransferCommandValidator().ValidateAndThrow(command);

        var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for client company '{command.ResolvedClientCompanyId}'.");

        var recipient = await recipients.GetByIdAsync(command.RecipientId, command.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"Recipient '{command.RecipientId}' not found.");

        if (recipient.Status != RecipientStatus.Active)
        {
            throw new ConflictException($"Recipient '{command.RecipientId}' is not Active (current status: {recipient.Status}).");
        }

        var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No fund account found for client company '{command.ResolvedClientCompanyId}'.");

        if (fundAccount.CurrencyCode != command.Amount.CurrencyCode || fundAccount.Balance < command.Amount.Amount)
        {
            throw new ConflictException($"Insufficient balance for transfer of {command.Amount.Amount} {command.Amount.CurrencyCode}.");
        }

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            command.ResolvedClientCompanyId,
            command.IdempotencyKey,
            new { command.RecipientId, command.Amount.Amount, command.Amount.CurrencyCode },
            unitOfWork,
            async () =>
            {
                var gatewayResult = await gateway.CreateTransferAsync(
                    new CreateTransferGatewayRequest(
                        command.IdempotencyKey, subAccount.CircleWalletId, recipient.CircleRecipientId!, command.Amount),
                    cancellationToken);

                var now = DateTime.UtcNow;
                var transfer = new Transfer
                {
                    Id = Guid.NewGuid(),
                    SubAccountId = subAccount.Id,
                    RecipientId = recipient.Id,
                    ClientCompanyId = command.ResolvedClientCompanyId,
                    Amount = command.Amount,
                    CircleTransferId = gatewayResult.CircleTransferId,
                    Status = TransferStatus.Pending,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                await transfers.AddAsync(transfer, cancellationToken);

                await transactions.AddAsync(new Transaction
                {
                    Id = Guid.NewGuid(),
                    SubAccountId = subAccount.Id,
                    ClientCompanyId = command.ResolvedClientCompanyId,
                    Type = TransactionType.Transfer,
                    Status = TransactionStatus.Pending,
                    Amount = command.Amount,
                    ProviderReferenceId = gatewayResult.CircleTransferId,
                    CorrelationId = command.CorrelationId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }, cancellationToken);

                await auditLog.AppendAsync(
                    "TransferCreated", "Transfer", transfer.Id.ToString(),
                    JsonSerializer.Serialize(new { transfer.RecipientId, transfer.Amount, transfer.Status }),
                    command.ResolvedClientCompanyId, command.CorrelationId, cancellationToken);

                return new CreateTransferResult(transfer.Id, transfer.RecipientId, transfer.Amount, transfer.Status);
            },
            cancellationToken);
    }
}
```

- [ ] **Step 12: Create `MockStablecoinGateway.CreateTransferAsync` and its tests**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockStablecoinGatewayTransferTests.cs
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;

public class MockStablecoinGatewayTransferTests
{
    [Fact]
    public async Task CreateTransferAsync_schedules_complete_webhook_and_returns_pending()
    {
        var scheduler = new RecordingMockWebhookScheduler();
        var options = Options.Create(new MockProviderOptions
        {
            Enabled = true, WebhookDelayMilliseconds = 0, ResponseLatencyMilliseconds = 0,
            FailureInjectionRate = 0, RejectBusinessNameSuffix = "REJECT",
        });
        var gateway = new MockStablecoinGateway(options, scheduler, new FixedRandomSource(0.99));

        var result = await gateway.CreateTransferAsync(
            new CreateTransferGatewayRequest("key-1", "wallet-1", "recipient-1", new TreasuryServiceOrchestrator.Domain.Money(100m, "USDC")),
            TestContext.Current.CancellationToken);

        Assert.Equal("pending", result.Status);
        Assert.Single(scheduler.Scheduled);
        Assert.Equal("transfers", scheduler.Scheduled[0].Topic);
    }

    [Fact]
    public async Task CreateTransferAsync_throws_ProviderUnavailableException_on_injected_failure()
    {
        var scheduler = new RecordingMockWebhookScheduler();
        var options = Options.Create(new MockProviderOptions
        {
            Enabled = true, WebhookDelayMilliseconds = 0, ResponseLatencyMilliseconds = 0,
            FailureInjectionRate = 0.5, RejectBusinessNameSuffix = "REJECT",
        });
        var gateway = new MockStablecoinGateway(options, scheduler, new FixedRandomSource(0.01));

        await Assert.ThrowsAsync<TreasuryServiceOrchestrator.Application.Exceptions.ProviderUnavailableException>(() =>
            gateway.CreateTransferAsync(
                new CreateTransferGatewayRequest("key-2", "wallet-1", "recipient-1", new TreasuryServiceOrchestrator.Domain.Money(100m, "USDC")),
                TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 13: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockStablecoinGatewayTransferTests"`
Expected: FAIL — build error, `MockStablecoinGateway.CreateTransferAsync` does not exist.

- [ ] **Step 14: Implement `MockStablecoinGateway.CreateTransferAsync`**

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs` — add:

```csharp
public async Task<CreateTransferGatewayResult> CreateTransferAsync(
    CreateTransferGatewayRequest request, CancellationToken cancellationToken)
{
    if (settings.ResponseLatencyMilliseconds > 0)
    {
        await Task.Delay(settings.ResponseLatencyMilliseconds, cancellationToken);
    }

    if (randomSource.NextDouble() < settings.FailureInjectionRate)
    {
        throw new ProviderUnavailableException("Mock stablecoin gateway injected a transient failure.");
    }

    var circleTransferId = $"transfer-{Guid.NewGuid():N}";
    var finalStatus = "complete";

    var payload = $$"""
        {"transfer":{"id":"{{circleTransferId}}","status":"{{finalStatus}}"}}
        """;
    webhookScheduler.Schedule(new ScheduledMockWebhook(
        "transfers", payload, TimeSpan.FromMilliseconds(settings.WebhookDelayMilliseconds)));

    return new CreateTransferGatewayResult(circleTransferId, "pending");
}
```

- [ ] **Step 15: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockStablecoinGatewayTransferTests"`
Expected: PASS

- [ ] **Step 16: Write the failing tests for `ListTransfersQueryHandler`/`GetTransferQueryHandler`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/ListTransfersQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Transfers;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Transfers;

public class ListTransfersQueryHandlerTests
{
    [Fact]
    public async Task Lists_transfers_for_resolved_sub_account()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        var transfers = Substitute.For<ITransferRepository>();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            LifecycleState = SubAccountLifecycleState.Active, IsDisabled = false,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(), SubAccountId = subAccount.Id, RecipientId = Guid.NewGuid(),
            ClientCompanyId = "acme-co", Amount = new Money(100m, "USDC"), Status = TransferStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(subAccount);
        transfers.ListForSubAccountAsync(subAccount.Id, Arg.Any<CancellationToken>()).Returns([transfer]);

        var handler = new ListTransfersQueryHandler(subAccounts, transfers);
        var result = await handler.HandleAsync(new ListTransfersQuery("acme-co"), TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(transfer.Id, result[0].Id);
    }
}
```

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/GetTransferQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Transfers;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Transfers;

public class GetTransferQueryHandlerTests
{
    [Fact]
    public async Task Returns_transfer_when_found()
    {
        var transfers = Substitute.For<ITransferRepository>();
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), RecipientId = Guid.NewGuid(),
            ClientCompanyId = "acme-co", Amount = new Money(100m, "USDC"), Status = TransferStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        transfers.GetByIdAsync(transfer.Id, "acme-co", Arg.Any<CancellationToken>()).Returns(transfer);

        var handler = new GetTransferQueryHandler(transfers);
        var result = await handler.HandleAsync(new GetTransferQuery("acme-co", transfer.Id), TestContext.Current.CancellationToken);

        Assert.Equal(transfer.Id, result.Id);
    }

    [Fact]
    public async Task Throws_NotFoundException_when_missing()
    {
        var transfers = Substitute.For<ITransferRepository>();
        transfers.GetByIdAsync(Arg.Any<Guid>(), "acme-co", Arg.Any<CancellationToken>()).Returns((Transfer?)null);

        var handler = new GetTransferQueryHandler(transfers);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetTransferQuery("acme-co", Guid.NewGuid()), TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 17: Run tests to verify they fail**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListTransfersQueryHandlerTests|*GetTransferQueryHandlerTests"`
Expected: FAIL — build error, `ListTransfersQuery(Handler)` / `GetTransferQuery(Handler)` do not exist.

- [ ] **Step 18: Implement `ListTransfersQuery`/`GetTransferQuery` and their handlers**

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/ListTransfersQuery.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed record ListTransfersQuery(string ResolvedClientCompanyId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/ListTransfersQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed class ListTransfersQueryHandler(
    ISubAccountRepository subAccounts, ITransferRepository transfers)
    : IQueryHandler<ListTransfersQuery, IReadOnlyList<Transfer>>
{
    public async Task<IReadOnlyList<Transfer>> HandleAsync(ListTransfersQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for client company '{query.ResolvedClientCompanyId}'.");

        return await transfers.ListForSubAccountAsync(subAccount.Id, cancellationToken);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/GetTransferQuery.cs
namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed record GetTransferQuery(string ResolvedClientCompanyId, Guid TransferId);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/GetTransferQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed class GetTransferQueryHandler(ITransferRepository transfers)
    : IQueryHandler<GetTransferQuery, Transfer>
{
    public async Task<Transfer> HandleAsync(GetTransferQuery query, CancellationToken cancellationToken = default) =>
        await transfers.GetByIdAsync(query.TransferId, query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"Transfer '{query.TransferId}' not found.");
}
```

- [ ] **Step 19: Run tests to verify they pass**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListTransfersQueryHandlerTests|*GetTransferQueryHandlerTests"`
Expected: PASS

- [ ] **Step 20: Write the failing test for `ProcessTransferStatusCommandHandler` (webhook-driven, no-op-safe)**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Transfers/ProcessTransferStatusCommandHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Transfers;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Transfers;

public class ProcessTransferStatusCommandHandlerTests
{
    private static (ITransferRepository Transfers, ITransactionRepository Transactions, IFundAccountRepository FundAccounts,
        IBalanceSnapshotRepository Snapshots, IAuditLogService AuditLog, IUnitOfWork UnitOfWork) NewSubstitutes() =>
        (Substitute.For<ITransferRepository>(), Substitute.For<ITransactionRepository>(), Substitute.For<IFundAccountRepository>(),
            Substitute.For<IBalanceSnapshotRepository>(), Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

    [Fact]
    public async Task Debits_fund_account_and_completes_transaction_when_transfer_completes()
    {
        var f = NewSubstitutes();
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), RecipientId = Guid.NewGuid(),
            ClientCompanyId = "acme-co", Amount = new Money(100m, "USDC"),
            CircleTransferId = "transfer-1", Status = TransferStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.Transfers.GetByCircleTransferIdAsync("transfer-1", Arg.Any<CancellationToken>()).Returns(transfer);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), SubAccountId = transfer.SubAccountId, ClientCompanyId = "acme-co",
            Type = TransactionType.Transfer, Status = TransactionStatus.Pending, Amount = transfer.Amount,
            ProviderReferenceId = "transfer-1", CorrelationId = "corr-1",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.Transactions.GetByProviderReferenceIdAsync("transfer-1", Arg.Any<CancellationToken>()).Returns(transaction);
        var fundAccount = new FundAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", Balance = 250m, CurrencyCode = "USDC",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.FundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(fundAccount);

        var handler = new ProcessTransferStatusCommandHandler(
            f.Transfers, f.Transactions, f.FundAccounts, f.Snapshots, f.AuditLog, f.UnitOfWork);
        var result = await handler.HandleAsync(
            new ProcessTransferStatusCommand("transfer-1", "complete"), TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Complete, result.Status);
        Assert.Equal(150m, fundAccount.Balance);
        Assert.Equal(TransactionStatus.Complete, transaction.Status);
        await f.Snapshots.Received(1).AddAsync(
            Arg.Is<BalanceSnapshot>(s => s.Balance.Amount == 150m && s.Reason == BalanceSnapshotReason.PostMutation),
            Arg.Any<CancellationToken>());
        await f.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_touch_balance_and_marks_transaction_failed_when_transfer_fails()
    {
        var f = NewSubstitutes();
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), RecipientId = Guid.NewGuid(),
            ClientCompanyId = "acme-co", Amount = new Money(100m, "USDC"),
            CircleTransferId = "transfer-1", Status = TransferStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.Transfers.GetByCircleTransferIdAsync("transfer-1", Arg.Any<CancellationToken>()).Returns(transfer);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), SubAccountId = transfer.SubAccountId, ClientCompanyId = "acme-co",
            Type = TransactionType.Transfer, Status = TransactionStatus.Pending, Amount = transfer.Amount,
            ProviderReferenceId = "transfer-1", CorrelationId = "corr-1",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.Transactions.GetByProviderReferenceIdAsync("transfer-1", Arg.Any<CancellationToken>()).Returns(transaction);

        var handler = new ProcessTransferStatusCommandHandler(
            f.Transfers, f.Transactions, f.FundAccounts, f.Snapshots, f.AuditLog, f.UnitOfWork);
        var result = await handler.HandleAsync(
            new ProcessTransferStatusCommand("transfer-1", "failed"), TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Failed, result.Status);
        Assert.Equal(TransactionStatus.Failed, transaction.Status);
        await f.FundAccounts.DidNotReceive().GetByClientCompanyIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f.Snapshots.DidNotReceive().AddAsync(Arg.Any<BalanceSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Is_a_no_op_when_status_unchanged()
    {
        var f = NewSubstitutes();
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(), SubAccountId = Guid.NewGuid(), RecipientId = Guid.NewGuid(),
            ClientCompanyId = "acme-co", Amount = new Money(100m, "USDC"),
            CircleTransferId = "transfer-1", Status = TransferStatus.Complete,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.Transfers.GetByCircleTransferIdAsync("transfer-1", Arg.Any<CancellationToken>()).Returns(transfer);

        var handler = new ProcessTransferStatusCommandHandler(
            f.Transfers, f.Transactions, f.FundAccounts, f.Snapshots, f.AuditLog, f.UnitOfWork);
        await handler.HandleAsync(new ProcessTransferStatusCommand("transfer-1", "complete"), TestContext.Current.CancellationToken);

        await f.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_NotFoundException_when_transfer_missing()
    {
        var f = NewSubstitutes();
        f.Transfers.GetByCircleTransferIdAsync("unknown", Arg.Any<CancellationToken>()).Returns((Transfer?)null);

        var handler = new ProcessTransferStatusCommandHandler(
            f.Transfers, f.Transactions, f.FundAccounts, f.Snapshots, f.AuditLog, f.UnitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new ProcessTransferStatusCommand("unknown", "complete"), TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 21: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessTransferStatusCommandHandlerTests"`
Expected: FAIL — build error, `ProcessTransferStatusCommand(Handler)` does not exist.

- [ ] **Step 22: Implement `TransferStatusMapper`, `ProcessTransferStatusCommand`/`Result`/`Handler`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/ProcessTransferStatusCommand.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed record ProcessTransferStatusCommand(string CircleTransferId, string Status);

public sealed record ProcessTransferStatusResult(Guid TransferId, TransferStatus Status);

internal static class TransferStatusMapper
{
    public static TransferStatus Map(string circleStatus) => circleStatus.ToLowerInvariant() switch
    {
        "pending" => TransferStatus.Pending,
        "running" => TransferStatus.Running,
        "complete" => TransferStatus.Complete,
        "failed" => TransferStatus.Failed,
        _ => throw new InvalidOperationException($"Unrecognized transfer status '{circleStatus}'."),
    };
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Transfers/ProcessTransferStatusCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Transfers;

public sealed class ProcessTransferStatusCommandHandler(
    ITransferRepository transfers,
    ITransactionRepository transactions,
    IFundAccountRepository fundAccounts,
    IBalanceSnapshotRepository snapshots,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>
{
    public async Task<ProcessTransferStatusResult> HandleAsync(
        ProcessTransferStatusCommand command, CancellationToken cancellationToken = default)
    {
        var transfer = await transfers.GetByCircleTransferIdAsync(command.CircleTransferId, cancellationToken)
            ?? throw new NotFoundException($"Transfer with Circle transfer id '{command.CircleTransferId}' not found.");

        var newStatus = TransferStatusMapper.Map(command.Status);
        if (transfer.Status == newStatus)
        {
            return new ProcessTransferStatusResult(transfer.Id, transfer.Status);
        }

        transfer.Status = newStatus;
        transfer.FailureReason = newStatus == TransferStatus.Failed ? "Transfer failed per Circle Mint webhook." : null;
        transfer.UpdatedAtUtc = DateTime.UtcNow;

        var transaction = await transactions.GetByProviderReferenceIdAsync(command.CircleTransferId, cancellationToken);
        if (transaction is not null)
        {
            transaction.Status = newStatus switch
            {
                TransferStatus.Complete => TransactionStatus.Complete,
                TransferStatus.Failed => TransactionStatus.Failed,
                _ => transaction.Status,
            };
            transaction.UpdatedAtUtc = transfer.UpdatedAtUtc;
        }

        if (newStatus == TransferStatus.Complete)
        {
            var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(transfer.ClientCompanyId, cancellationToken)
                ?? throw new NotFoundException($"No fund account found for client company '{transfer.ClientCompanyId}'.");

            fundAccount.Balance -= transfer.Amount.Amount;
            fundAccount.UpdatedAtUtc = transfer.UpdatedAtUtc;

            await snapshots.AddAsync(new BalanceSnapshot
            {
                Id = Guid.NewGuid(),
                SubAccountId = transfer.SubAccountId,
                ClientCompanyId = transfer.ClientCompanyId,
                Balance = new Money(fundAccount.Balance, fundAccount.CurrencyCode),
                Reason = BalanceSnapshotReason.PostMutation,
                CapturedAtUtc = transfer.UpdatedAtUtc,
            }, cancellationToken);
        }

        await auditLog.AppendAsync(
            "TransferStatusChanged", "Transfer", transfer.Id.ToString(),
            JsonSerializer.Serialize(new { transfer.Status, transfer.FailureReason }),
            transfer.ClientCompanyId, correlationId: command.CircleTransferId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessTransferStatusResult(transfer.Id, transfer.Status);
    }
}
```

- [ ] **Step 23: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessTransferStatusCommandHandlerTests"`
Expected: PASS

- [ ] **Step 24: Write the failing test for `TransfersWebhookTopicProcessor`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/TransfersWebhookTopicProcessorTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Transfers;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;

public class TransfersWebhookTopicProcessorTests
{
    [Fact]
    public void Topic_is_transfers()
    {
        var processor = new TransfersWebhookTopicProcessor(Substitute.For<TreasuryServiceOrchestrator.Application.ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>>());
        Assert.Equal("transfers", processor.Topic);
    }

    [Fact]
    public async Task ProcessAsync_deserializes_payload_and_invokes_handler()
    {
        var handler = Substitute.For<TreasuryServiceOrchestrator.Application.ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>>();
        var processor = new TransfersWebhookTopicProcessor(handler);

        var payload = """{"transfer":{"id":"transfer-1","status":"complete"}}""";
        await processor.ProcessAsync(payload, TestContext.Current.CancellationToken);

        await handler.Received(1).HandleAsync(
            Arg.Is<ProcessTransferStatusCommand>(c => c.CircleTransferId == "transfer-1" && c.Status == "complete"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_throws_InvalidOperationException_when_payload_missing_fields()
    {
        var handler = Substitute.For<TreasuryServiceOrchestrator.Application.ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>>();
        var processor = new TransfersWebhookTopicProcessor(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync("""{"transfer":{}}""", TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 25: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*TransfersWebhookTopicProcessorTests"`
Expected: FAIL — build error, `TransfersWebhookTopicProcessor` does not exist.

- [ ] **Step 26: Implement `TransfersWebhookTopicProcessor`**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Webhooks/TransfersWebhookTopicProcessor.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Application.Transfers;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

public sealed class TransfersWebhookTopicProcessor(
    ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult> decisionHandler) : IWebhookTopicProcessor
{
    public string Topic => "transfers";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<TransfersPayload>(payloadJson)
            ?? throw new InvalidOperationException("transfers webhook payload deserialized to null.");

        if (payload.Transfer?.Id is null || payload.Transfer.Status is null)
        {
            throw new InvalidOperationException("transfers webhook payload missing transfer id or status.");
        }

        await decisionHandler.HandleAsync(
            new ProcessTransferStatusCommand(payload.Transfer.Id, payload.Transfer.Status), cancellationToken);
    }

    private sealed record TransfersPayload
    {
        [JsonPropertyName("transfer")]
        public TransferPayload? Transfer { get; init; }
    }

    private sealed record TransferPayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
```

- [ ] **Step 27: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*TransfersWebhookTopicProcessorTests"`
Expected: PASS

- [ ] **Step 28: Implement `TransferRepository`, `DbContext` mapping, `Program.cs` DI wiring, and `TransfersController`**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TransferRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class TransferRepository(TreasuryServiceOrchestratorDbContext dbContext) : ITransferRepository
{
    public async Task AddAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        await dbContext.Transfers.AddAsync(transfer, cancellationToken);
    }

    public Task<Transfer?> GetByIdAsync(Guid id, string clientCompanyId, CancellationToken cancellationToken) =>
        dbContext.Transfers.SingleOrDefaultAsync(
            t => t.Id == id && t.ClientCompanyId == clientCompanyId, cancellationToken);

    public Task<Transfer?> GetByCircleTransferIdAsync(string circleTransferId, CancellationToken cancellationToken) =>
        dbContext.Transfers.SingleOrDefaultAsync(t => t.CircleTransferId == circleTransferId, cancellationToken);

    public async Task<IReadOnlyList<Transfer>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken) =>
        await dbContext.Transfers.Where(t => t.SubAccountId == subAccountId).ToListAsync(cancellationToken);
}
```

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs` — add the DbSet:

```csharp
public DbSet<Transfer> Transfers => Set<Transfer>();
```

and, inside `OnModelCreating`, add:

```csharp
modelBuilder.Entity<Transfer>(builder =>
{
    builder.HasKey(e => e.Id);
    builder.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
    builder.HasIndex(e => e.SubAccountId);
    builder.HasIndex(e => e.CircleTransferId).IsUnique();
});
```

Modify `src/TreasuryServiceOrchestrator.Api/Program.cs` — add:

```csharp
builder.Services.AddScoped<ITransferRepository, TransferRepository>();
builder.Services.AddScoped<ICommandHandler<CreateTransferCommand, CreateTransferResult>, CreateTransferCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListTransfersQuery, IReadOnlyList<Transfer>>, ListTransfersQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetTransferQuery, Transfer>, GetTransferQueryHandler>();
builder.Services.AddScoped<ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>, ProcessTransferStatusCommandHandler>();
builder.Services.AddScoped<IWebhookTopicProcessor, TransfersWebhookTopicProcessor>();
```

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/TransfersController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Api.Tenancy;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Transfers;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/transfers")]
public sealed class TransfersController(
    ICallerContext caller,
    ICommandHandler<CreateTransferCommand, CreateTransferResult> createHandler,
    IQueryHandler<ListTransfersQuery, IReadOnlyList<Transfer>> listHandler,
    IQueryHandler<GetTransferQuery, Transfer> getHandler) : ControllerBase
{
    public sealed record CreateTransferRequest(Guid RecipientId, decimal Amount, string CurrencyCode, string IdempotencyKey);

    [HttpPost]
    public async Task<IActionResult> Create(
        string clientCompanyId, [FromBody] CreateTransferRequest request, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var correlationId = HttpContext.TraceIdentifier;

        var result = await createHandler.HandleAsync(
            new CreateTransferCommand(
                resolved, request.RecipientId, new Money(request.Amount, request.CurrencyCode),
                request.IdempotencyKey, correlationId),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { clientCompanyId, transferId = result.TransferId }, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(string clientCompanyId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var result = await listHandler.HandleAsync(new ListTransfersQuery(resolved), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{transferId:guid}")]
    public async Task<IActionResult> Get(string clientCompanyId, Guid transferId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var result = await getHandler.HandleAsync(new GetTransferQuery(resolved, transferId), cancellationToken);
        return Ok(result);
    }
}
```

- [ ] **Step 29: Regenerate the `InitialCreate` migration**

```bash
dotnet ef migrations remove --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --force
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
```
Expected: a single regenerated `InitialCreate` migration whose `Up` method now includes a `Transfers` table with `ClientCompanyId` using `Latin1_General_100_BIN2` collation and a unique index on `CircleTransferId`, alongside all pre-existing tables.

Run: `dotnet ef migrations has-pending-model-changes --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api`
Expected: `No changes`.

- [ ] **Step 30: Write the failing integration test, then run the full suite**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/TransfersEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class TransfersEndpointsTests(SqlServerTestDatabaseFixture fixture)
{
    [Fact]
    public async Task Create_then_get_shows_transfer_transitioning_to_Complete_via_mock_webhook()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "Tenant";
            config["MockProvider:Enabled"] = "true";
            config["MockProvider:WebhookDelayMilliseconds"] = "0";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");

        await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co", new
        {
            BusinessName = "Acme Co", BusinessUniqueIdentifier = "acme-ein-1", IdempotencyKey = "sub-acme-1",
        }, TestContext.Current.CancellationToken);

        var registerResponse = await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co/recipients", new
        {
            Chain = "ETH", Address = "0xdeadbeef", Label = "Vendor Payout Wallet", IdempotencyKey = "recipient-1",
        }, TestContext.Current.CancellationToken);
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterRecipientResponse>(TestContext.Current.CancellationToken);

        RecipientResponse? recipient = null;
        for (var attempt = 0; attempt < 10 && recipient?.Status != "Active"; attempt++)
        {
            recipient = await client.GetFromJsonAsync<RecipientResponse>(
                $"/api/v1/sub-accounts/acme-co/recipients/{registered!.RecipientId}", TestContext.Current.CancellationToken);
            if (recipient?.Status != "Active")
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        Assert.Equal("Active", recipient!.Status);

        var createResponse = await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co/transfers", new
        {
            RecipientId = recipient.Id, Amount = 50m, CurrencyCode = "USDC", IdempotencyKey = "transfer-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateTransferResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("Pending", created!.Status);

        TransferResponse? transfer = null;
        for (var attempt = 0; attempt < 10 && transfer?.Status != "Complete"; attempt++)
        {
            transfer = await client.GetFromJsonAsync<TransferResponse>(
                $"/api/v1/sub-accounts/acme-co/transfers/{created.TransferId}", TestContext.Current.CancellationToken);
            if (transfer?.Status != "Complete")
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        Assert.Equal("Complete", transfer!.Status);
    }

    private sealed record RegisterRecipientResponse(Guid RecipientId, string Chain, string Address, string Label, string Status);
    private sealed record RecipientResponse(Guid Id, string Chain, string Address, string Label, string Status);
    private sealed record CreateTransferResponse(Guid TransferId, Guid RecipientId, MoneyResponse Amount, string Status);
    private sealed record TransferResponse(Guid Id, Guid RecipientId, MoneyResponse Amount, string Status);
    private sealed record MoneyResponse(decimal Amount, string CurrencyCode);
}
```
The retry-poll loop mirrors the pattern established in Task 9's `RecipientsEndpointsTests` — `MockProvider:WebhookDelayMilliseconds` is `0` so the scheduled webhook fires as soon as the background dispatcher's next tick runs.

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 31: Commit**

```bash
git add -A
git commit -m "feat: outbound transfer creation/list/get, transfers webhook topic processor, regenerate InitialCreate migration"
```

---

## Task 11: Redemption rework (gross/fees/net) + LinkedBankAccount (PRD §5, §8)

Reworks `RedeemRequest` to carry gross/fees/net separately (Circle's Institutional Direct flat fee is only known once the payout webhook lands) and adds the minimal `LinkedBankAccount` surface Redemption needs as its destination account. Redemption uses `POST /v1/businessAccount/payouts` (PRD §8.1) — **not** the Travel-Rule-gated `POST /v1/payouts`, so no originator fields are added here. Mirrors Task 10's transfer slice: gateway call returns immediately with `Pending`, the mock schedules a `"payouts"` webhook, and the webhook-driven handler debits the fund account by the **gross** amount and records fees/net only on completion.

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Domain/RedeemRequest.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/LinkedBankAccount.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/LinkedBankAccountStatus.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ILinkedBankAccountRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IRedeemRequestRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/IStablecoinGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderOptions.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/CreateLinkedBankAccountCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/CreateLinkedBankAccountCommandValidator.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/CreateLinkedBankAccountCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/ListLinkedBankAccountsQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/GetLinkedBankAccountQuery.cs`
- Delete: `src/TreasuryServiceOrchestrator.Application/Redeem/CreateRedeemCommand.cs`, `CreateRedeemCommandHandler.cs`, `CreateRedeemValidator.cs`, `CreateRedeemResult.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Redemptions/CreateRedemptionCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Redemptions/CreateRedemptionCommandValidator.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Redemptions/CreateRedemptionCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Redemptions/ListRedemptionsQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Redemptions/GetRedemptionQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Redemptions/ProcessPayoutStatusCommand.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Redemptions/ProcessPayoutStatusCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Webhooks/PayoutsWebhookTopicProcessor.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/LinkedBankAccountRepository.cs`
- Delete: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/RedeemRequestRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/RedeemRequestRepository.cs` (rewritten)
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/LinkedBankAccountsController.cs`
- Delete: `src/TreasuryServiceOrchestrator.Api/Controllers/RedeemController.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/RedemptionsController.cs`
- Modify: `tests/TreasuryServiceOrchestrator.IntegrationTests/CrossTenantRedeemIsolationTests.cs`
- Create: `tests/TreasuryServiceOrchestrator.IntegrationTests/RedemptionsEndpointsTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/LinkedBankAccounts/CreateLinkedBankAccountCommandHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Redemptions/CreateRedemptionCommandHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Redemptions/ProcessPayoutStatusCommandHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockStablecoinGatewayRedeemTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/PayoutsWebhookTopicProcessorTests.cs`

**Interfaces:**
- Consumes: `ISubAccountRepository.GetByClientCompanyIdAsync`, `IFundAccountRepository.GetByClientCompanyIdAsync`, `IAuditLogService.AppendAsync`, `IdempotencyExecutor.ExecuteAsync`, `IUnitOfWork`, `TenantScopeResolver.Resolve`, `ICallerContext` (all Task 1/8/9/10).
- Produces: `LinkedBankAccount { Guid Id; string BeneficiaryName; string AccountNumber; string RoutingNumber; string BankName; string CircleBankAccountId; LinkedBankAccountStatus Status; DateTime CreatedAtUtc; DateTime UpdatedAtUtc; }`; `RedeemRequest` reworked with `Guid SubAccountId`, `Guid LinkedBankAccountId`, `Money GrossAmount`, `Money? Fees`, `Money? NetAmount`, `string? FailureReason`; `CreateRedemptionCommand`/`CreateRedemptionResult`; `ProcessPayoutStatusCommand`/`Result` consumed by Task 14's notification outbox the same way `ProcessTransferStatusCommand` is.

- [ ] **Step 1: Rework the `RedeemRequest` domain entity and add `LinkedBankAccount`**

```csharp
// src/TreasuryServiceOrchestrator.Domain/RedeemRequest.cs
namespace TreasuryServiceOrchestrator.Domain;

public class RedeemRequest
{
    public Guid Id { get; set; }
    public required string ClientCompanyId { get; set; }
    public Guid SubAccountId { get; set; }
    public Guid LinkedBankAccountId { get; set; }
    public required string CircleRedeemId { get; set; }
    public Money GrossAmount { get; set; }
    public Money? Fees { get; set; }
    public Money? NetAmount { get; set; }
    public TransferStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/LinkedBankAccountStatus.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum LinkedBankAccountStatus
{
    Pending,
    Active,
    Failed,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/LinkedBankAccount.cs
namespace TreasuryServiceOrchestrator.Domain;

public class LinkedBankAccount
{
    public Guid Id { get; set; }
    public required string BeneficiaryName { get; set; }
    public required string AccountNumber { get; set; }
    public required string RoutingNumber { get; set; }
    public required string BankName { get; set; }
    public required string CircleBankAccountId { get; set; }
    public LinkedBankAccountStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

`LinkedBankAccount` carries **no `ClientCompanyId`** — per PRD line 141, it is a Distributor-level entity shared across tenants, unlike every other entity in this system.

- [ ] **Step 2: Add `ILinkedBankAccountRepository` and rework `IRedeemRequestRepository`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ILinkedBankAccountRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface ILinkedBankAccountRepository
{
    Task AddAsync(LinkedBankAccount account, CancellationToken cancellationToken);

    Task<LinkedBankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LinkedBankAccount>> ListAsync(CancellationToken cancellationToken);
}
```

Replace `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IRedeemRequestRepository.cs` with:

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Ports/IRedeemRequestRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IRedeemRequestRepository
{
    Task AddAsync(RedeemRequest request, CancellationToken cancellationToken);

    Task<RedeemRequest?> GetByIdAsync(Guid id, string clientCompanyId, CancellationToken cancellationToken);

    Task<RedeemRequest?> GetByCircleRedeemIdAsync(string circleRedeemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RedeemRequest>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken);
}
```

This drops the old unscoped `GetByIdAsync(Guid, ct)` — every caller must now prove tenant ownership, matching `ITransferRepository`.

- [ ] **Step 3: Rework `GatewayDtos.cs` and `IStablecoinGateway`**

Replace the existing `RedeemGatewayRequest`/`GatewayRedeemResult` records in `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs` with:

```csharp
public sealed record RedeemGatewayRequest(
    string IdempotencyKey, string SourceWalletId, string DestinationBankAccountId, Money GrossAmount);

public sealed record GatewayRedeemResult(string CircleRedeemId, string Status);

public sealed record CreateLinkedBankAccountGatewayRequest(
    string BeneficiaryName, string AccountNumber, string RoutingNumber, string BankName);

public sealed record CreateLinkedBankAccountGatewayResult(string CircleBankAccountId, string Status);
```

(Leave `TransferStatusResult` and the `ExternalEntity*` DTOs untouched.)

Replace `src/TreasuryServiceOrchestrator.Application/Shared/Ports/IStablecoinGateway.cs` with:

```csharp
namespace TreasuryServiceOrchestrator.Application.Shared.Ports;

public interface IStablecoinGateway
{
    Task<GatewayRedeemResult> RedeemAsync(RedeemGatewayRequest request, CancellationToken cancellationToken);

    Task<CreateLinkedBankAccountGatewayResult> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken cancellationToken);

    Task<TransferStatusResult> GetTransferStatusAsync(string transferId, CancellationToken cancellationToken);

    Task<CreateTransferGatewayResult> CreateTransferAsync(
        CreateTransferGatewayRequest request, CancellationToken cancellationToken);
}
```

(`GetTransferStatusAsync`/`CreateTransferAsync` are unchanged from Task 10 — repeated here only because this step replaces the whole file.)

- [ ] **Step 4: Rework `CircleMintGateway.RedeemAsync` and stub `CreateLinkedBankAccountAsync`**

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs` — replace the existing `RedeemAsync` method with:

```csharp
public Task<GatewayRedeemResult> RedeemAsync(RedeemGatewayRequest request, CancellationToken cancellationToken) =>
    Task.FromResult(new GatewayRedeemResult(
        CircleRedeemId: $"redeem-{Guid.NewGuid():N}", Status: "pending"));

public Task<CreateLinkedBankAccountGatewayResult> CreateLinkedBankAccountAsync(
    CreateLinkedBankAccountGatewayRequest request, CancellationToken cancellationToken) =>
    Task.FromResult(new CreateLinkedBankAccountGatewayResult(
        CircleBankAccountId: $"bank-account-{Guid.NewGuid():N}", Status: "complete"));
```

(The real Circle Mint HTTP calls for both are deferred to Phase 3 — see `docs/circle-mint-docs/`.)

- [ ] **Step 5: Add `RedemptionFlatFeeAmount` to `MockProviderOptions`**

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderOptions.cs` — add a property:

```csharp
public decimal RedemptionFlatFeeAmount { get; set; } = 1.50m;
```

- [ ] **Step 6: Write the failing tests for `MockStablecoinGateway.RedeemAsync` and `CreateLinkedBankAccountAsync`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/MockStablecoinGatewayRedeemTests.cs
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Ports;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;

public class MockStablecoinGatewayRedeemTests
{
    private sealed class FixedRandomSource(double value) : IMockRandomSource
    {
        public double NextDouble() => value;
    }

    [Fact]
    public async Task RedeemAsync_schedules_payouts_webhook_and_returns_pending()
    {
        var scheduler = new RecordingMockWebhookScheduler();
        var options = Options.Create(new MockProviderOptions
        {
            WebhookDelayMilliseconds = 0, ResponseLatencyMilliseconds = 0,
            FailureInjectionRate = 0, RedemptionFlatFeeAmount = 1.50m,
        });
        var gateway = new MockStablecoinGateway(options, scheduler, new FixedRandomSource(0.99));

        var result = await gateway.RedeemAsync(
            new RedeemGatewayRequest("key-1", "wallet-1", "bank-account-1", new Money(100m, "USDC")),
            TestContext.Current.CancellationToken);

        Assert.Equal("pending", result.Status);
        Assert.Single(scheduler.Scheduled);
        Assert.Equal("payouts", scheduler.Scheduled[0].Topic);
        Assert.Contains(result.CircleRedeemId, scheduler.Scheduled[0].PayloadJson);
        Assert.Contains("1.50", scheduler.Scheduled[0].PayloadJson);
    }

    [Fact]
    public async Task RedeemAsync_throws_ProviderUnavailableException_on_injected_failure()
    {
        var scheduler = new RecordingMockWebhookScheduler();
        var options = Options.Create(new MockProviderOptions
        {
            WebhookDelayMilliseconds = 0, ResponseLatencyMilliseconds = 0, FailureInjectionRate = 0.5,
        });
        var gateway = new MockStablecoinGateway(options, scheduler, new FixedRandomSource(0.01));

        await Assert.ThrowsAsync<TreasuryServiceOrchestrator.Application.Exceptions.ProviderUnavailableException>(() =>
            gateway.RedeemAsync(
                new RedeemGatewayRequest("key-2", "wallet-1", "bank-account-1", new Money(100m, "USDC")),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateLinkedBankAccountAsync_returns_complete_synchronously()
    {
        var scheduler = new RecordingMockWebhookScheduler();
        var options = Options.Create(new MockProviderOptions
        {
            WebhookDelayMilliseconds = 0, ResponseLatencyMilliseconds = 0, FailureInjectionRate = 0,
        });
        var gateway = new MockStablecoinGateway(options, scheduler, new FixedRandomSource(0.99));

        var result = await gateway.CreateLinkedBankAccountAsync(
            new CreateLinkedBankAccountGatewayRequest("Acme Co", "000123456789", "021000021", "Chase"),
            TestContext.Current.CancellationToken);

        Assert.Equal("complete", result.Status);
        Assert.NotEmpty(result.CircleBankAccountId);
        Assert.Empty(scheduler.Scheduled);
    }
}
```

`RecordingMockWebhookScheduler` and `IMockWebhookScheduler`/`ScheduledMockWebhook` already exist from Task 6/10.

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockStablecoinGatewayRedeemTests"`
Expected: FAIL — build error, `MockStablecoinGateway.RedeemAsync`/`CreateLinkedBankAccountAsync` don't match the new signatures.

- [ ] **Step 8: Rewrite `MockStablecoinGateway`**

Replace `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs` in full (this file has now been touched by Task 6, Task 10, and this task — shown here as its full cumulative state):

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Mocks;

public sealed class MockStablecoinGateway(
    IOptions<MockProviderOptions> options,
    IMockWebhookScheduler webhookScheduler,
    IMockRandomSource randomSource) : IStablecoinGateway
{
    private readonly ConcurrentDictionary<string, TransferStatus> _statusByTransferId = new();

    public async Task<CreateTransferGatewayResult> CreateTransferAsync(
        CreateTransferGatewayRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (settings.ResponseLatencyMilliseconds > 0)
        {
            await Task.Delay(settings.ResponseLatencyMilliseconds, cancellationToken);
        }

        if (randomSource.NextDouble() < settings.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock stablecoin gateway injected a transient failure.");
        }

        var circleTransferId = $"transfer-{Guid.NewGuid():N}";
        var finalStatus = "complete";

        var payload = $$"""
            {"transfer":{"id":"{{circleTransferId}}","status":"{{finalStatus}}"}}
            """;
        webhookScheduler.Schedule(new ScheduledMockWebhook(
            "transfers", payload, TimeSpan.FromMilliseconds(settings.WebhookDelayMilliseconds)));

        return new CreateTransferGatewayResult(circleTransferId, "pending");
    }

    public async Task<GatewayRedeemResult> RedeemAsync(RedeemGatewayRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (settings.ResponseLatencyMilliseconds > 0)
        {
            await Task.Delay(settings.ResponseLatencyMilliseconds, cancellationToken);
        }

        if (randomSource.NextDouble() < settings.FailureInjectionRate)
        {
            throw new ProviderUnavailableException("Mock stablecoin gateway injected a transient failure.");
        }

        var circleRedeemId = $"redeem-{Guid.NewGuid():N}";
        var finalStatus = "complete";
        var fees = settings.RedemptionFlatFeeAmount;
        var netAmount = request.GrossAmount.Amount - fees;

        var payload = $$"""
            {"payout":{"id":"{{circleRedeemId}}","status":"{{finalStatus}}","amount":"{{request.GrossAmount.Amount}}","fees":"{{fees}}","netAmount":"{{netAmount}}","currency":"{{request.GrossAmount.CurrencyCode}}"}}
            """;
        webhookScheduler.Schedule(new ScheduledMockWebhook(
            "payouts", payload, TimeSpan.FromMilliseconds(settings.WebhookDelayMilliseconds)));

        return new GatewayRedeemResult(circleRedeemId, "pending");
    }

    public Task<CreateLinkedBankAccountGatewayResult> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new CreateLinkedBankAccountGatewayResult(
            CircleBankAccountId: $"bank-account-{Guid.NewGuid():N}", Status: "complete"));

    public Task<TransferStatusResult> GetTransferStatusAsync(string transferId, CancellationToken cancellationToken)
    {
        var status = _statusByTransferId.TryGetValue(transferId, out var s) ? s : TransferStatus.Pending;
        return Task.FromResult(new TransferStatusResult(transferId, status));
    }
}
```

~~`CreateLinkedBankAccountAsync` completes synchronously (no webhook)~~ **Corrected 2026-07-17:** wrong — Circle's bank-account verification is asynchronous, delivered on the `wire` webhook topic (`pending → complete | failed`). The mock gateway returns `pending` on create and the emitter schedules a `wire` webhook for the decision. See correction 6 in the header block.

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*MockStablecoinGatewayRedeemTests"`
Expected: PASS (3 tests).

- [ ] **Step 10: Write the failing test for `CreateLinkedBankAccountCommandHandler`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/LinkedBankAccounts/CreateLinkedBankAccountCommandHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.LinkedBankAccounts;

public class CreateLinkedBankAccountCommandHandlerTests
{
    [Fact]
    public async Task Creates_linked_bank_account_via_gateway_and_persists_it()
    {
        var gateway = Substitute.For<IStablecoinGateway>();
        gateway.CreateLinkedBankAccountAsync(Arg.Any<CreateLinkedBankAccountGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateLinkedBankAccountGatewayResult("bank-account-1", "complete"));
        var repository = Substitute.For<ILinkedBankAccountRepository>();
        var auditLog = Substitute.For<IAuditLogService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var handler = new CreateLinkedBankAccountCommandHandler(gateway, repository, auditLog, unitOfWork);
        var command = new CreateLinkedBankAccountCommand("Acme Co", "000123456789", "021000021", "Chase", "corr-1");

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal("bank-account-1", result.CircleBankAccountId);
        Assert.Equal(LinkedBankAccountStatus.Active, result.Status);
        await repository.Received(1).AddAsync(
            Arg.Is<LinkedBankAccount>(a => a.BankName == "Chase" && a.Status == LinkedBankAccountStatus.Active),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 11: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*CreateLinkedBankAccountCommandHandlerTests"`
Expected: FAIL — build error, `TreasuryServiceOrchestrator.Application.LinkedBankAccounts` namespace doesn't exist.

- [ ] **Step 12: Implement `CreateLinkedBankAccountCommand`/`Validator`/`Handler` and the list/get queries**

```csharp
// src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/CreateLinkedBankAccountCommand.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.LinkedBankAccounts;

public sealed record CreateLinkedBankAccountCommand(
    string BeneficiaryName, string AccountNumber, string RoutingNumber, string BankName, string CorrelationId);

public sealed record CreateLinkedBankAccountResult(Guid LinkedBankAccountId, string CircleBankAccountId, LinkedBankAccountStatus Status);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/CreateLinkedBankAccountCommandValidator.cs
using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.LinkedBankAccounts;

public sealed class CreateLinkedBankAccountCommandValidator : AbstractValidator<CreateLinkedBankAccountCommand>
{
    public CreateLinkedBankAccountCommandValidator()
    {
        RuleFor(x => x.BeneficiaryName).NotEmpty();
        RuleFor(x => x.AccountNumber).NotEmpty();
        RuleFor(x => x.RoutingNumber).NotEmpty();
        RuleFor(x => x.BankName).NotEmpty();
        RuleFor(x => x.CorrelationId).NotEmpty();
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/CreateLinkedBankAccountCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.LinkedBankAccounts;

internal static class LinkedBankAccountStatusMapper
{
    public static LinkedBankAccountStatus Map(string circleStatus) => circleStatus.ToLowerInvariant() switch
    {
        "pending" => LinkedBankAccountStatus.Pending,
        "complete" => LinkedBankAccountStatus.Active,
        "failed" => LinkedBankAccountStatus.Failed,
        _ => throw new InvalidOperationException($"Unrecognized linked bank account status '{circleStatus}'."),
    };
}

public sealed class CreateLinkedBankAccountCommandHandler(
    IStablecoinGateway gateway,
    ILinkedBankAccountRepository linkedBankAccounts,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateLinkedBankAccountCommand, CreateLinkedBankAccountResult>
{
    // LinkedBankAccount has no ClientCompanyId (PRD line 141: Distributor-level, not tenant-scoped),
    // so it cannot use IdempotencyExecutor (which requires a (ClientCompanyId, IdempotencyKey) tuple).
    // A single direct SaveChangesAsync is used instead of the two-phase idempotency pattern.
    private const string AdminAuditSentinel = "ADMIN";

    public async Task<CreateLinkedBankAccountResult> HandleAsync(
        CreateLinkedBankAccountCommand command, CancellationToken cancellationToken = default)
    {
        new CreateLinkedBankAccountCommandValidator().ValidateAndThrow(command);

        var gatewayResult = await gateway.CreateLinkedBankAccountAsync(
            new CreateLinkedBankAccountGatewayRequest(
                command.BeneficiaryName, command.AccountNumber, command.RoutingNumber, command.BankName),
            cancellationToken);

        var now = DateTime.UtcNow;
        var linkedBankAccount = new LinkedBankAccount
        {
            Id = Guid.NewGuid(),
            BeneficiaryName = command.BeneficiaryName,
            AccountNumber = command.AccountNumber,
            RoutingNumber = command.RoutingNumber,
            BankName = command.BankName,
            CircleBankAccountId = gatewayResult.CircleBankAccountId,
            Status = LinkedBankAccountStatusMapper.Map(gatewayResult.Status),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await linkedBankAccounts.AddAsync(linkedBankAccount, cancellationToken);

        await auditLog.AppendAsync(
            "LinkedBankAccountCreated", "LinkedBankAccount", linkedBankAccount.Id.ToString(),
            JsonSerializer.Serialize(new { linkedBankAccount.BankName, linkedBankAccount.Status }),
            AdminAuditSentinel, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateLinkedBankAccountResult(linkedBankAccount.Id, linkedBankAccount.CircleBankAccountId, linkedBankAccount.Status);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/ListLinkedBankAccountsQuery.cs
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.LinkedBankAccounts;

public sealed record ListLinkedBankAccountsQuery;

public sealed class ListLinkedBankAccountsQueryHandler(ILinkedBankAccountRepository linkedBankAccounts)
    : IQueryHandler<ListLinkedBankAccountsQuery, IReadOnlyList<LinkedBankAccount>>
{
    public async Task<IReadOnlyList<LinkedBankAccount>> HandleAsync(
        ListLinkedBankAccountsQuery query, CancellationToken cancellationToken = default) =>
        await linkedBankAccounts.ListAsync(cancellationToken);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/LinkedBankAccounts/GetLinkedBankAccountQuery.cs
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.LinkedBankAccounts;

public sealed record GetLinkedBankAccountQuery(Guid LinkedBankAccountId);

public sealed class GetLinkedBankAccountQueryHandler(ILinkedBankAccountRepository linkedBankAccounts)
    : IQueryHandler<GetLinkedBankAccountQuery, LinkedBankAccount>
{
    public async Task<LinkedBankAccount> HandleAsync(GetLinkedBankAccountQuery query, CancellationToken cancellationToken = default) =>
        await linkedBankAccounts.GetByIdAsync(query.LinkedBankAccountId, cancellationToken)
            ?? throw new NotFoundException($"Linked bank account '{query.LinkedBankAccountId}' not found.");
}
```

- [ ] **Step 13: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*CreateLinkedBankAccountCommandHandlerTests"`
Expected: PASS

- [ ] **Step 14: Delete the old Redeem slice and write the failing test for `CreateRedemptionCommandHandler`**

Delete `src/TreasuryServiceOrchestrator.Application/Redeem/CreateRedeemCommand.cs`, `CreateRedeemCommandHandler.cs`, `CreateRedeemValidator.cs`, `CreateRedeemResult.cs` and the now-empty `Redeem/` folder.

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Redemptions/CreateRedemptionCommandHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Redemptions;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Redemptions;

public class CreateRedemptionCommandHandlerTests
{
    private sealed record Fixtures(
        ISubAccountRepository SubAccounts, ILinkedBankAccountRepository LinkedBankAccounts,
        IFundAccountRepository FundAccounts, IRedeemRequestRepository RedeemRequests,
        IStablecoinGateway Gateway, IIdempotencyService Idempotency, IAuditLogService AuditLog, IUnitOfWork UnitOfWork);

    private static Fixtures NewSubstitutes() => new(
        Substitute.For<ISubAccountRepository>(), Substitute.For<ILinkedBankAccountRepository>(),
        Substitute.For<IFundAccountRepository>(), Substitute.For<IRedeemRequestRepository>(),
        Substitute.For<IStablecoinGateway>(), Substitute.For<IIdempotencyService>(),
        Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

    private static CreateRedemptionCommandHandler NewHandler(Fixtures f) => new(
        f.SubAccounts, f.LinkedBankAccounts, f.FundAccounts, f.RedeemRequests, f.Gateway, f.Idempotency, f.AuditLog, f.UnitOfWork);

    [Fact]
    public async Task Creates_redemption_pending_and_debits_nothing_yet()
    {
        var f = NewSubstitutes();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            ComplianceState = SubAccountComplianceState.Accepted, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var linkedBankAccount = new LinkedBankAccount
        {
            Id = Guid.NewGuid(), BeneficiaryName = "Acme Co", AccountNumber = "000123456789",
            RoutingNumber = "021000021", BankName = "Chase", CircleBankAccountId = "bank-account-1",
            Status = LinkedBankAccountStatus.Active, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var fundAccount = new FundAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", CurrencyCode = "USDC", Balance = 500m, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.SubAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(subAccount);
        f.LinkedBankAccounts.GetByIdAsync(linkedBankAccount.Id, Arg.Any<CancellationToken>()).Returns(linkedBankAccount);
        f.FundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(fundAccount);
        f.Gateway.RedeemAsync(Arg.Any<RedeemGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRedeemResult("redeem-1", "pending"));
        f.Idempotency.TryReserveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = NewHandler(f);
        var command = new CreateRedemptionCommand("acme-co", linkedBankAccount.Id, 100m, "USD", "idem-1", "corr-1");

        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Pending, result.Status);
        Assert.Equal("redeem-1", result.CircleRedeemId);
        Assert.Equal(100m, result.GrossAmount.Amount);
        await f.RedeemRequests.Received(1).AddAsync(
            Arg.Is<RedeemRequest>(r => r.Fees == null && r.NetAmount == null && r.Status == TransferStatus.Pending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_ConflictException_when_linked_bank_account_not_Active()
    {
        var f = NewSubstitutes();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            ComplianceState = SubAccountComplianceState.Accepted, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var linkedBankAccount = new LinkedBankAccount
        {
            Id = Guid.NewGuid(), BeneficiaryName = "Acme Co", AccountNumber = "000123456789",
            RoutingNumber = "021000021", BankName = "Chase", CircleBankAccountId = "bank-account-1",
            Status = LinkedBankAccountStatus.Pending, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.SubAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(subAccount);
        f.LinkedBankAccounts.GetByIdAsync(linkedBankAccount.Id, Arg.Any<CancellationToken>()).Returns(linkedBankAccount);

        var handler = NewHandler(f);
        var command = new CreateRedemptionCommand("acme-co", linkedBankAccount.Id, 100m, "USD", "idem-1", "corr-1");

        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_ConflictException_when_fund_account_balance_insufficient()
    {
        var f = NewSubstitutes();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            ComplianceState = SubAccountComplianceState.Accepted, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var linkedBankAccount = new LinkedBankAccount
        {
            Id = Guid.NewGuid(), BeneficiaryName = "Acme Co", AccountNumber = "000123456789",
            RoutingNumber = "021000021", BankName = "Chase", CircleBankAccountId = "bank-account-1",
            Status = LinkedBankAccountStatus.Active, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var fundAccount = new FundAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", CurrencyCode = "USDC", Balance = 50m, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.SubAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(subAccount);
        f.LinkedBankAccounts.GetByIdAsync(linkedBankAccount.Id, Arg.Any<CancellationToken>()).Returns(linkedBankAccount);
        f.FundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(fundAccount);

        var handler = NewHandler(f);
        var command = new CreateRedemptionCommand("acme-co", linkedBankAccount.Id, 100m, "USD", "idem-1", "corr-1");

        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(command, TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 15: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*CreateRedemptionCommandHandlerTests"`
Expected: FAIL — build error, `TreasuryServiceOrchestrator.Application.Redemptions` namespace, `CreateRedemptionCommand`, `ILinkedBankAccountRepository`-taking constructor don't exist.

- [ ] **Step 16: Implement `CreateRedemptionCommand`/`Validator`/`Handler`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Redemptions/CreateRedemptionCommand.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Redemptions;

public sealed record CreateRedemptionCommand(
    string ResolvedClientCompanyId, Guid LinkedBankAccountId, decimal GrossAmount,
    string TargetFiatCurrencyCode, string IdempotencyKey, string CorrelationId);

public sealed record CreateRedemptionResult(Guid RedemptionId, string CircleRedeemId, Money GrossAmount, TransferStatus Status);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Redemptions/CreateRedemptionCommandValidator.cs
using FluentValidation;

namespace TreasuryServiceOrchestrator.Application.Redemptions;

public sealed class CreateRedemptionCommandValidator : AbstractValidator<CreateRedemptionCommand>
{
    public CreateRedemptionCommandValidator()
    {
        RuleFor(x => x.ResolvedClientCompanyId).NotEmpty();
        RuleFor(x => x.LinkedBankAccountId).NotEmpty();
        RuleFor(x => x.GrossAmount).GreaterThan(0m);
        RuleFor(x => x.TargetFiatCurrencyCode).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Redemptions/CreateRedemptionCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Redemptions;

public sealed class CreateRedemptionCommandHandler(
    ISubAccountRepository subAccounts,
    ILinkedBankAccountRepository linkedBankAccounts,
    IFundAccountRepository fundAccounts,
    IRedeemRequestRepository redeemRequests,
    IStablecoinGateway gateway,
    IIdempotencyService idempotency,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateRedemptionCommand, CreateRedemptionResult>
{
    public async Task<CreateRedemptionResult> HandleAsync(CreateRedemptionCommand command, CancellationToken cancellationToken = default)
    {
        new CreateRedemptionCommandValidator().ValidateAndThrow(command);

        var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for client company '{command.ResolvedClientCompanyId}'.");

        var linkedBankAccount = await linkedBankAccounts.GetByIdAsync(command.LinkedBankAccountId, cancellationToken)
            ?? throw new NotFoundException($"Linked bank account '{command.LinkedBankAccountId}' not found.");

        if (linkedBankAccount.Status != LinkedBankAccountStatus.Active)
        {
            throw new ConflictException($"Linked bank account '{command.LinkedBankAccountId}' is not Active (current status: {linkedBankAccount.Status}).");
        }

        var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(command.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No fund account found for client company '{command.ResolvedClientCompanyId}'.");

        if (fundAccount.CurrencyCode != "USDC" || fundAccount.Balance < command.GrossAmount)
        {
            throw new ConflictException($"Insufficient balance for redemption of {command.GrossAmount} USDC.");
        }

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            command.ResolvedClientCompanyId,
            command.IdempotencyKey,
            new { command.LinkedBankAccountId, command.GrossAmount, command.TargetFiatCurrencyCode },
            unitOfWork,
            async () =>
            {
                var grossMoney = new Money(command.GrossAmount, "USDC");
                var gatewayResult = await gateway.RedeemAsync(
                    new RedeemGatewayRequest(
                        command.IdempotencyKey, subAccount.CircleWalletId!, linkedBankAccount.CircleBankAccountId, grossMoney),
                    cancellationToken);

                var now = DateTime.UtcNow;
                var redeemRequest = new RedeemRequest
                {
                    Id = Guid.NewGuid(),
                    ClientCompanyId = command.ResolvedClientCompanyId,
                    SubAccountId = subAccount.Id,
                    LinkedBankAccountId = linkedBankAccount.Id,
                    CircleRedeemId = gatewayResult.CircleRedeemId,
                    GrossAmount = grossMoney,
                    Fees = null,
                    NetAmount = null,
                    Status = TransferStatus.Pending,
                    CorrelationId = command.CorrelationId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                await redeemRequests.AddAsync(redeemRequest, cancellationToken);

                await auditLog.AppendAsync(
                    "RedemptionCreated", "RedeemRequest", redeemRequest.Id.ToString(),
                    JsonSerializer.Serialize(new { redeemRequest.CircleRedeemId, redeemRequest.GrossAmount, redeemRequest.Status }),
                    command.ResolvedClientCompanyId, command.CorrelationId, cancellationToken);

                return new CreateRedemptionResult(redeemRequest.Id, redeemRequest.CircleRedeemId, redeemRequest.GrossAmount, redeemRequest.Status);
            },
            cancellationToken);
    }
}
```

The gross-amount debit does **not** happen here — it happens in `ProcessPayoutStatusCommandHandler` (Step 20) only once the webhook confirms completion, mirroring `CreateTransferCommandHandler`/`ProcessTransferStatusCommandHandler` from Task 10.

- [ ] **Step 17: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*CreateRedemptionCommandHandlerTests"`
Expected: PASS (3 tests).

- [ ] **Step 18: Add `ListRedemptionsQuery`/`GetRedemptionQuery`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Redemptions/ListRedemptionsQuery.cs
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Redemptions;

public sealed record ListRedemptionsQuery(string ResolvedClientCompanyId);

public sealed class ListRedemptionsQueryHandler(ISubAccountRepository subAccounts, IRedeemRequestRepository redeemRequests)
    : IQueryHandler<ListRedemptionsQuery, IReadOnlyList<RedeemRequest>>
{
    public async Task<IReadOnlyList<RedeemRequest>> HandleAsync(ListRedemptionsQuery query, CancellationToken cancellationToken = default)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account found for client company '{query.ResolvedClientCompanyId}'.");

        return await redeemRequests.ListForSubAccountAsync(subAccount.Id, cancellationToken);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Redemptions/GetRedemptionQuery.cs
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Redemptions;

public sealed record GetRedemptionQuery(string ResolvedClientCompanyId, Guid RedemptionId);

public sealed class GetRedemptionQueryHandler(IRedeemRequestRepository redeemRequests)
    : IQueryHandler<GetRedemptionQuery, RedeemRequest>
{
    public async Task<RedeemRequest> HandleAsync(GetRedemptionQuery query, CancellationToken cancellationToken = default) =>
        await redeemRequests.GetByIdAsync(query.RedemptionId, query.ResolvedClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"Redemption '{query.RedemptionId}' not found.");
}
```

- [ ] **Step 19: Write the failing test for `ProcessPayoutStatusCommandHandler`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Redemptions/ProcessPayoutStatusCommandHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Redemptions;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Redemptions;

public class ProcessPayoutStatusCommandHandlerTests
{
    private sealed record Fixtures(
        IRedeemRequestRepository RedeemRequests, IFundAccountRepository FundAccounts,
        IBalanceSnapshotRepository Snapshots, IAuditLogService AuditLog, IUnitOfWork UnitOfWork);

    private static Fixtures NewSubstitutes() => new(
        Substitute.For<IRedeemRequestRepository>(), Substitute.For<IFundAccountRepository>(),
        Substitute.For<IBalanceSnapshotRepository>(), Substitute.For<IAuditLogService>(), Substitute.For<IUnitOfWork>());

    [Fact]
    public async Task Marks_Complete_records_fees_and_net_and_debits_gross_from_fund_account()
    {
        var f = NewSubstitutes();
        var redeemRequest = new RedeemRequest
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", SubAccountId = Guid.NewGuid(),
            LinkedBankAccountId = Guid.NewGuid(), CircleRedeemId = "redeem-1",
            GrossAmount = new Money(100m, "USDC"), Status = TransferStatus.Pending,
            CorrelationId = "corr-1", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var fundAccount = new FundAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", CurrencyCode = "USDC", Balance = 500m, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.RedeemRequests.GetByCircleRedeemIdAsync("redeem-1", Arg.Any<CancellationToken>()).Returns(redeemRequest);
        f.FundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns(fundAccount);

        var handler = new ProcessPayoutStatusCommandHandler(f.RedeemRequests, f.FundAccounts, f.Snapshots, f.AuditLog, f.UnitOfWork);
        await handler.HandleAsync(
            new ProcessPayoutStatusCommand("redeem-1", "complete", 100m, 1.50m, 98.50m, "USD"),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Complete, redeemRequest.Status);
        Assert.Equal(1.50m, redeemRequest.Fees!.Amount);
        Assert.Equal(98.50m, redeemRequest.NetAmount!.Amount);
        Assert.Equal(400m, fundAccount.Balance);
        await f.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Is_idempotent_when_status_already_matches()
    {
        var f = NewSubstitutes();
        var redeemRequest = new RedeemRequest
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", SubAccountId = Guid.NewGuid(),
            LinkedBankAccountId = Guid.NewGuid(), CircleRedeemId = "redeem-1",
            GrossAmount = new Money(100m, "USDC"), Fees = new Money(1.50m, "USD"), NetAmount = new Money(98.50m, "USD"),
            Status = TransferStatus.Complete, CorrelationId = "corr-1", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        f.RedeemRequests.GetByCircleRedeemIdAsync("redeem-1", Arg.Any<CancellationToken>()).Returns(redeemRequest);

        var handler = new ProcessPayoutStatusCommandHandler(f.RedeemRequests, f.FundAccounts, f.Snapshots, f.AuditLog, f.UnitOfWork);
        await handler.HandleAsync(
            new ProcessPayoutStatusCommand("redeem-1", "complete", 100m, 1.50m, 98.50m, "USD"),
            TestContext.Current.CancellationToken);

        await f.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_NotFoundException_when_redemption_missing()
    {
        var f = NewSubstitutes();
        f.RedeemRequests.GetByCircleRedeemIdAsync("unknown", Arg.Any<CancellationToken>()).Returns((RedeemRequest?)null);

        var handler = new ProcessPayoutStatusCommandHandler(f.RedeemRequests, f.FundAccounts, f.Snapshots, f.AuditLog, f.UnitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new ProcessPayoutStatusCommand("unknown", "complete", 100m, 1.50m, 98.50m, "USD"), TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 20: Run test to verify it fails, then implement `ProcessPayoutStatusCommand`/`Handler`**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessPayoutStatusCommandHandlerTests"`
Expected: FAIL — build error, `ProcessPayoutStatusCommand`/`Handler` don't exist.

```csharp
// src/TreasuryServiceOrchestrator.Application/Redemptions/ProcessPayoutStatusCommand.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Redemptions;

public sealed record ProcessPayoutStatusCommand(
    string CircleRedeemId, string Status, decimal GrossAmount, decimal Fees, decimal NetAmount, string FiatCurrencyCode);

public sealed record ProcessPayoutStatusResult(Guid RedemptionId, TransferStatus Status);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Redemptions/ProcessPayoutStatusCommandHandler.cs
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Transfers;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Redemptions;

public sealed class ProcessPayoutStatusCommandHandler(
    IRedeemRequestRepository redeemRequests,
    IFundAccountRepository fundAccounts,
    IBalanceSnapshotRepository snapshots,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>
{
    public async Task<ProcessPayoutStatusResult> HandleAsync(
        ProcessPayoutStatusCommand command, CancellationToken cancellationToken = default)
    {
        var redeemRequest = await redeemRequests.GetByCircleRedeemIdAsync(command.CircleRedeemId, cancellationToken)
            ?? throw new NotFoundException($"Redemption with Circle redeem id '{command.CircleRedeemId}' not found.");

        var newStatus = TransferStatusMapper.Map(command.Status);
        if (redeemRequest.Status == newStatus)
        {
            return new ProcessPayoutStatusResult(redeemRequest.Id, redeemRequest.Status);
        }

        redeemRequest.Status = newStatus;
        redeemRequest.FailureReason = newStatus == TransferStatus.Failed ? "Redemption failed per Circle Mint webhook." : null;
        redeemRequest.UpdatedAtUtc = DateTime.UtcNow;

        if (newStatus == TransferStatus.Complete)
        {
            redeemRequest.Fees = new Money(command.Fees, command.FiatCurrencyCode);
            redeemRequest.NetAmount = new Money(command.NetAmount, command.FiatCurrencyCode);

            var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(redeemRequest.ClientCompanyId, cancellationToken)
                ?? throw new NotFoundException($"No fund account found for client company '{redeemRequest.ClientCompanyId}'.");

            fundAccount.Balance -= redeemRequest.GrossAmount.Amount;
            fundAccount.UpdatedAtUtc = redeemRequest.UpdatedAtUtc;

            await snapshots.AddAsync(new BalanceSnapshot
            {
                Id = Guid.NewGuid(),
                SubAccountId = redeemRequest.SubAccountId,
                ClientCompanyId = redeemRequest.ClientCompanyId,
                Balance = new Money(fundAccount.Balance, fundAccount.CurrencyCode),
                Reason = BalanceSnapshotReason.PostMutation,
                CapturedAtUtc = redeemRequest.UpdatedAtUtc,
            }, cancellationToken);
        }

        await auditLog.AppendAsync(
            "RedemptionStatusChanged", "RedeemRequest", redeemRequest.Id.ToString(),
            JsonSerializer.Serialize(new { redeemRequest.Status, redeemRequest.Fees, redeemRequest.NetAmount, redeemRequest.FailureReason }),
            redeemRequest.ClientCompanyId, correlationId: command.CircleRedeemId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessPayoutStatusResult(redeemRequest.Id, redeemRequest.Status);
    }
}
```

`TransferStatusMapper` is `internal` in `TreasuryServiceOrchestrator.Application.Transfers` (Task 10) — same assembly, so it's reusable here without duplicating the switch statement.

- [ ] **Step 21: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ProcessPayoutStatusCommandHandlerTests"`
Expected: PASS (3 tests).

- [ ] **Step 22: Write the failing test for `PayoutsWebhookTopicProcessor`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/PayoutsWebhookTopicProcessorTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Redemptions;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure;

public class PayoutsWebhookTopicProcessorTests
{
    [Fact]
    public void Topic_is_payouts()
    {
        var processor = new PayoutsWebhookTopicProcessor(Substitute.For<TreasuryServiceOrchestrator.Application.ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>>());
        Assert.Equal("payouts", processor.Topic);
    }

    [Fact]
    public async Task ProcessAsync_deserializes_payload_and_invokes_handler()
    {
        var handler = Substitute.For<TreasuryServiceOrchestrator.Application.ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>>();
        var processor = new PayoutsWebhookTopicProcessor(handler);

        var payload = """{"payout":{"id":"redeem-1","status":"complete","amount":"100","fees":"1.50","netAmount":"98.50","currency":"USD"}}""";
        await processor.ProcessAsync(payload, TestContext.Current.CancellationToken);

        await handler.Received(1).HandleAsync(
            Arg.Is<ProcessPayoutStatusCommand>(c =>
                c.CircleRedeemId == "redeem-1" && c.Status == "complete" &&
                c.GrossAmount == 100m && c.Fees == 1.50m && c.NetAmount == 98.50m && c.FiatCurrencyCode == "USD"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_throws_InvalidOperationException_when_payload_missing_fields()
    {
        var handler = Substitute.For<TreasuryServiceOrchestrator.Application.ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>>();
        var processor = new PayoutsWebhookTopicProcessor(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync("""{"payout":{}}""", TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 23: Run test to verify it fails, then implement `PayoutsWebhookTopicProcessor`**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*PayoutsWebhookTopicProcessorTests"`
Expected: FAIL — build error, `PayoutsWebhookTopicProcessor` doesn't exist.

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Webhooks/PayoutsWebhookTopicProcessor.cs
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Redemptions;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

public sealed class PayoutsWebhookTopicProcessor(
    ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult> decisionHandler) : IWebhookTopicProcessor
{
    public string Topic => "payouts";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PayoutsPayload>(payloadJson)
            ?? throw new InvalidOperationException("payouts webhook payload deserialized to null.");

        if (payload.Payout?.Id is null || payload.Payout.Status is null || payload.Payout.Amount is null ||
            payload.Payout.Fees is null || payload.Payout.NetAmount is null || payload.Payout.Currency is null)
        {
            throw new InvalidOperationException("payouts webhook payload missing required fields.");
        }

        await decisionHandler.HandleAsync(
            new ProcessPayoutStatusCommand(
                payload.Payout.Id, payload.Payout.Status,
                decimal.Parse(payload.Payout.Amount, CultureInfo.InvariantCulture),
                decimal.Parse(payload.Payout.Fees, CultureInfo.InvariantCulture),
                decimal.Parse(payload.Payout.NetAmount, CultureInfo.InvariantCulture),
                payload.Payout.Currency),
            cancellationToken);
    }

    private sealed record PayoutsPayload
    {
        [JsonPropertyName("payout")]
        public PayoutPayload? Payout { get; init; }
    }

    private sealed record PayoutPayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("amount")]
        public string? Amount { get; init; }

        [JsonPropertyName("fees")]
        public string? Fees { get; init; }

        [JsonPropertyName("netAmount")]
        public string? NetAmount { get; init; }

        [JsonPropertyName("currency")]
        public string? Currency { get; init; }
    }
}
```

- [ ] **Step 24: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*PayoutsWebhookTopicProcessorTests"`
Expected: PASS (3 tests).

- [ ] **Step 25: Implement `LinkedBankAccountRepository` and rewrite `RedeemRequestRepository`**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/LinkedBankAccountRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class LinkedBankAccountRepository(TreasuryServiceOrchestratorDbContext dbContext) : ILinkedBankAccountRepository
{
    public async Task AddAsync(LinkedBankAccount account, CancellationToken cancellationToken)
    {
        await dbContext.LinkedBankAccounts.AddAsync(account, cancellationToken);
    }

    public Task<LinkedBankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LinkedBankAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LinkedBankAccount>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.LinkedBankAccounts.ToListAsync(cancellationToken);
}
```

Replace `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/RedeemRequestRepository.cs` with:

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/RedeemRequestRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class RedeemRequestRepository(TreasuryServiceOrchestratorDbContext dbContext) : IRedeemRequestRepository
{
    public async Task AddAsync(RedeemRequest request, CancellationToken cancellationToken)
    {
        await dbContext.RedeemRequests.AddAsync(request, cancellationToken);
    }

    public Task<RedeemRequest?> GetByIdAsync(Guid id, string clientCompanyId, CancellationToken cancellationToken) =>
        dbContext.RedeemRequests.SingleOrDefaultAsync(
            r => r.Id == id && r.ClientCompanyId == clientCompanyId, cancellationToken);

    public Task<RedeemRequest?> GetByCircleRedeemIdAsync(string circleRedeemId, CancellationToken cancellationToken) =>
        dbContext.RedeemRequests.SingleOrDefaultAsync(r => r.CircleRedeemId == circleRedeemId, cancellationToken);

    public async Task<IReadOnlyList<RedeemRequest>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken) =>
        await dbContext.RedeemRequests.Where(r => r.SubAccountId == subAccountId).ToListAsync(cancellationToken);
}
```

- [ ] **Step 26: Rework the `RedeemRequest` mapping and add the `LinkedBankAccounts` mapping in `TreasuryServiceOrchestratorDbContext`**

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs` — replace the `DbSet<RedeemRequest> RedeemRequests` line with:

```csharp
public DbSet<RedeemRequest> RedeemRequests => Set<RedeemRequest>();
public DbSet<LinkedBankAccount> LinkedBankAccounts => Set<LinkedBankAccount>();
```

Replace the existing `modelBuilder.Entity<RedeemRequest>(entity => { ... })` block with:

```csharp
modelBuilder.Entity<RedeemRequest>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.ComplexProperty(e => e.GrossAmount, cp =>
    {
        cp.Property(m => m.Amount).HasColumnName("gross_amount_value").HasColumnType("decimal(18,6)");
        cp.Property(m => m.CurrencyCode).HasColumnName("gross_currency_code").HasMaxLength(4);
    });
    entity.ComplexProperty(e => e.Fees, cp =>
    {
        cp.IsRequired(false);
        cp.Property(m => m.Amount).HasColumnName("fees_value").HasColumnType("decimal(18,6)");
        cp.Property(m => m.CurrencyCode).HasColumnName("fees_currency_code").HasMaxLength(3);
    });
    entity.ComplexProperty(e => e.NetAmount, cp =>
    {
        cp.IsRequired(false);
        cp.Property(m => m.Amount).HasColumnName("net_amount_value").HasColumnType("decimal(18,6)");
        cp.Property(m => m.CurrencyCode).HasColumnName("net_currency_code").HasMaxLength(3);
    });
    entity.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
    entity.HasIndex(e => e.SubAccountId);
    entity.HasIndex(e => e.CircleRedeemId).IsUnique();
    entity.HasIndex(e => e.CorrelationId);
    entity.HasIndex(e => e.ClientCompanyId);
    // Defense-in-depth only — handlers enforce ownership explicitly.
    // Background/webhook code with no HTTP request has no ITenantContext.ClientCompanyId and
    // must call .IgnoreQueryFilters() deliberately when looking up by a provider id,
    // then carry the row's own ClientCompanyId forward explicitly. Never introduce an ambient
    // tenant value for non-request code paths.
    entity.HasQueryFilter(e => e.ClientCompanyId == tenantContext.ClientCompanyId);
});

modelBuilder.Entity<LinkedBankAccount>(entity =>
{
    entity.HasKey(e => e.Id);
    // No ClientCompanyId column/collation/query filter — Distributor-level, shared across tenants (PRD line 141).
    entity.HasIndex(e => e.CircleBankAccountId).IsUnique();
});
```

`cp.IsRequired(false)` marks the `ComplexProperty` itself nullable so `Fees`/`NetAmount` map to nullable columns until the payout webhook populates them — EF Core's `ComplexProperty` API models a `null` complex property as all-underlying-columns-null, which is safe here since `Fees`/`NetAmount` are only ever read after the null check on `RedeemRequest.Status == TransferStatus.Complete`.

- [ ] **Step 27: Wire DI registrations in `Program.cs`**

Modify `src/TreasuryServiceOrchestrator.Api/Program.cs` — add:

```csharp
builder.Services.AddScoped<ILinkedBankAccountRepository, LinkedBankAccountRepository>();
builder.Services.AddScoped<IRedeemRequestRepository, RedeemRequestRepository>();
builder.Services.AddScoped<ICommandHandler<CreateLinkedBankAccountCommand, CreateLinkedBankAccountResult>, CreateLinkedBankAccountCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListLinkedBankAccountsQuery, IReadOnlyList<LinkedBankAccount>>, ListLinkedBankAccountsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetLinkedBankAccountQuery, LinkedBankAccount>, GetLinkedBankAccountQueryHandler>();
builder.Services.AddScoped<ICommandHandler<CreateRedemptionCommand, CreateRedemptionResult>, CreateRedemptionCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListRedemptionsQuery, IReadOnlyList<RedeemRequest>>, ListRedemptionsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetRedemptionQuery, RedeemRequest>, GetRedemptionQueryHandler>();
builder.Services.AddScoped<ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>, ProcessPayoutStatusCommandHandler>();
builder.Services.AddScoped<IWebhookTopicProcessor, PayoutsWebhookTopicProcessor>();
```

Remove the old `CreateRedeemCommand`/`Handler` registration and the `RedeemController` route it backed.

- [ ] **Step 28: Replace `RedeemController` with `RedemptionsController`, add `LinkedBankAccountsController`**

Delete `src/TreasuryServiceOrchestrator.Api/Controllers/RedeemController.cs`.

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/RedemptionsController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Api.Tenancy;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Redemptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/redemptions")]
public sealed class RedemptionsController(
    ICallerContext caller,
    ICommandHandler<CreateRedemptionCommand, CreateRedemptionResult> createHandler,
    IQueryHandler<ListRedemptionsQuery, IReadOnlyList<RedeemRequest>> listHandler,
    IQueryHandler<GetRedemptionQuery, RedeemRequest> getHandler) : ControllerBase
{
    public sealed record CreateRedemptionRequest(Guid LinkedBankAccountId, decimal GrossAmount, string TargetFiatCurrencyCode, string IdempotencyKey);

    [HttpPost]
    public async Task<IActionResult> Create(
        string clientCompanyId, [FromBody] CreateRedemptionRequest request, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var correlationId = HttpContext.TraceIdentifier;

        var result = await createHandler.HandleAsync(
            new CreateRedemptionCommand(
                resolved, request.LinkedBankAccountId, request.GrossAmount, request.TargetFiatCurrencyCode,
                request.IdempotencyKey, correlationId),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { clientCompanyId, redemptionId = result.RedemptionId }, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(string clientCompanyId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var result = await listHandler.HandleAsync(new ListRedemptionsQuery(resolved), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{redemptionId:guid}")]
    public async Task<IActionResult> Get(string clientCompanyId, Guid redemptionId, CancellationToken cancellationToken)
    {
        var resolved = TenantScopeResolver.Resolve(caller, clientCompanyId)!;
        var result = await getHandler.HandleAsync(new GetRedemptionQuery(resolved, redemptionId), cancellationToken);
        return Ok(result);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/LinkedBankAccountsController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/linked-bank-accounts")]
public sealed class LinkedBankAccountsController(
    ICallerContext caller,
    ICommandHandler<CreateLinkedBankAccountCommand, CreateLinkedBankAccountResult> createHandler,
    IQueryHandler<ListLinkedBankAccountsQuery, IReadOnlyList<LinkedBankAccount>> listHandler,
    IQueryHandler<GetLinkedBankAccountQuery, LinkedBankAccount> getHandler) : ControllerBase
{
    public sealed record CreateLinkedBankAccountRequest(string BeneficiaryName, string AccountNumber, string RoutingNumber, string BankName);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLinkedBankAccountRequest request, CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
        {
            throw new TenantForbiddenException("Only Admin may create linked bank accounts.");
        }

        var result = await createHandler.HandleAsync(
            new CreateLinkedBankAccountCommand(
                request.BeneficiaryName, request.AccountNumber, request.RoutingNumber, request.BankName, HttpContext.TraceIdentifier),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { linkedBankAccountId = result.LinkedBankAccountId }, result);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
        {
            throw new TenantForbiddenException("Only Admin may view linked bank accounts.");
        }

        var result = await listHandler.HandleAsync(new ListLinkedBankAccountsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{linkedBankAccountId:guid}")]
    public async Task<IActionResult> Get(Guid linkedBankAccountId, CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
        {
            throw new TenantForbiddenException("Only Admin may view linked bank accounts.");
        }

        var result = await getHandler.HandleAsync(new GetLinkedBankAccountQuery(linkedBankAccountId), cancellationToken);
        return Ok(result);
    }
}
```

`LinkedBankAccountsController` uses a **flat** route (no `{clientCompanyId}` segment) — matching the entity's Distributor-level, non-tenant-scoped nature.

- [ ] **Step 29: Regenerate the `InitialCreate` migration**

```bash
dotnet ef migrations remove --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --force
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
```
Expected: a single regenerated `InitialCreate` migration whose `Up` method has `RedeemRequests` with `gross_amount_value`/`fees_value` (nullable)/`net_amount_value` (nullable) columns and a unique index on `CircleRedeemId`, plus a `LinkedBankAccounts` table with **no** `ClientCompanyId` column and a unique index on `CircleBankAccountId`.

Run: `dotnet ef migrations has-pending-model-changes --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api`
Expected: `No changes`.

- [ ] **Step 30: Update `CrossTenantRedeemIsolationTests.cs` for the new route**

Modify `tests/TreasuryServiceOrchestrator.IntegrationTests/CrossTenantRedeemIsolationTests.cs` — replace every `PostAsJsonAsync("/api/v1/redeem", ...)` call with `PostAsJsonAsync("/api/v1/sub-accounts/acme-co/redemptions", ...)` (using whichever tenant the test's header sets), and replace the request body's field names (`UsdcAmount`/`TargetFiatCurrencyCode`) with `LinkedBankAccountId`/`GrossAmount`/`TargetFiatCurrencyCode` — seeding a `LinkedBankAccount` via `POST /api/v1/linked-bank-accounts` (Admin header) before each redemption POST, since a redemption now requires a real `LinkedBankAccountId`.

- [ ] **Step 31: Write the failing integration test, then run the full suite**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/RedemptionsEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class RedemptionsEndpointsTests(SqlServerTestDatabaseFixture fixture)
{
    [Fact]
    public async Task Create_then_get_shows_redemption_transitioning_to_Complete_with_fees_and_net_via_mock_webhook()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "Tenant";
            config["KnownClientCompanies:1:Id"] = "admin";
            config["KnownClientCompanies:1:Role"] = "Admin";
            config["MockProvider:Enabled"] = "true";
            config["MockProvider:WebhookDelayMilliseconds"] = "0";
            config["MockProvider:RedemptionFlatFeeAmount"] = "1.50";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");

        await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co", new
        {
            BusinessName = "Acme Co", BusinessUniqueIdentifier = "acme-ein-1", IdempotencyKey = "sub-acme-1",
        }, TestContext.Current.CancellationToken);

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("ClientCompanyId", "admin");
        var bankResponse = await adminClient.PostAsJsonAsync("/api/v1/linked-bank-accounts", new
        {
            BeneficiaryName = "Acme Co", AccountNumber = "000123456789", RoutingNumber = "021000021", BankName = "Chase",
        }, TestContext.Current.CancellationToken);
        var bank = await bankResponse.Content.ReadFromJsonAsync<LinkedBankAccountResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("Active", bank!.Status);

        var createResponse = await client.PostAsJsonAsync("/api/v1/sub-accounts/acme-co/redemptions", new
        {
            LinkedBankAccountId = bank.LinkedBankAccountId, GrossAmount = 100m, TargetFiatCurrencyCode = "USD", IdempotencyKey = "redeem-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateRedemptionResponse>(TestContext.Current.CancellationToken);
        Assert.Equal("Pending", created!.Status);

        RedemptionResponse? redemption = null;
        for (var attempt = 0; attempt < 10 && redemption?.Status != "Complete"; attempt++)
        {
            redemption = await client.GetFromJsonAsync<RedemptionResponse>(
                $"/api/v1/sub-accounts/acme-co/redemptions/{created.RedemptionId}", TestContext.Current.CancellationToken);
            if (redemption?.Status != "Complete")
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        Assert.Equal("Complete", redemption!.Status);
        Assert.Equal(1.50m, redemption.Fees!.Amount);
        Assert.Equal(98.50m, redemption.NetAmount!.Amount);
    }

    private sealed record LinkedBankAccountResponse(Guid LinkedBankAccountId, string CircleBankAccountId, string Status);
    private sealed record CreateRedemptionResponse(Guid RedemptionId, string CircleRedeemId, MoneyResponse GrossAmount, string Status);
    private sealed record RedemptionResponse(Guid Id, string CircleRedeemId, MoneyResponse GrossAmount, MoneyResponse? Fees, MoneyResponse? NetAmount, string Status);
    private sealed record MoneyResponse(decimal Amount, string CurrencyCode);
}
```

The retry-poll loop mirrors `TransfersEndpointsTests` (Task 10) — `MockProvider:WebhookDelayMilliseconds` is `0` so the scheduled `"payouts"` webhook fires as soon as the background dispatcher's next tick runs.

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 32: Commit**

```bash
git add -A
git commit -m "feat: rework redemption for gross/fees/net, add LinkedBankAccount, payouts webhook topic processor, regenerate InitialCreate migration"
```

---

> **Note:** PRD §9's balances (current + history) and transactions (list/get) surface is already fully delivered by Task 8 (`TransactionsController`, `BalancesController`, `GetCurrentBalanceQuery`, `GetBalanceHistoryQuery`, `ListTransactionsQuery`, `GetTransactionQuery`) — Task 11's redemption rework and Task 10's transfers both write onto that same ledger. No separate task is needed; numbering continues below without a gap.

---

## Task 12: Admin cross-tenant views + master-account summary (PRD §2.5)

Adds the three admin-only "see everything" reads PRD §2.5 lists that no earlier task covers: a current-balance summary column on the existing all-sub-accounts list, an all-tenants transaction view (`GET /transactions?clientCompanyId=...`, filter optional), and `GET /master-account/summary` (main-wallet balance + sum of every sub-account's latest balance snapshot). The drill-down row ("one sub-account, admin names the target") needs no new code — every tenant-scoped read endpoint already resolves an Admin-named target via `TenantScopeResolver.Resolve` (Tasks 1-2).

Scope note: PRD §2.5's table also lists `/master-account/deposits`, `/master-account/wire-instructions`, and `/master-account/bank-accounts`. None of those has a backing entity anywhere in this plan (no Distributor-level deposit ledger, no `WireInstruction` entity was ever introduced), and §15.1 slice 8's own title is "**master-account summary**", not the full `/master-account/*` surface. Building them now would mean inventing unModeled entities with no consumer — deferred until a future phase actually needs them. `/master-account/bank-accounts` already has a real equivalent: Task 11's `GET /api/v1/linked-bank-accounts` (Admin-only, Distributor-level) — no second route is added for the same data.

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountResult.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/ListSubAccountsQueryHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ITransactionRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TransactionRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Ledger/ListAllTransactionsQuery.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/IStablecoinGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderOptions.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Admin/GetMasterAccountSummaryQuery.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Admin/GetMasterAccountSummaryQueryHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Controllers/TransactionsController.cs` (new admin-only sibling route added in a new controller, see below)
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/AdminTransactionsController.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/MasterAccountController.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/ListSubAccountsQueryHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Application/Admin/GetMasterAccountSummaryQueryHandlerTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/AdminCrossTenantViewsTests.cs`

**Interfaces:**
- Consumes: `ISubAccountRepository.ListAsync`/`GetByClientCompanyIdAsync`, `IFundAccountRepository.GetByClientCompanyIdAsync`, `IBalanceSnapshotRepository.GetLatestAsync`, `ICallerContext`/`TenantScopeResolver.Resolve` (Tasks 1-2, 8).
- Produces: `GetSubAccountResult` gains `Money? CurrentBalance`; `ITransactionRepository.ListAllAsync(string? clientCompanyId, TransactionType?, TransactionStatus?, DateTime?, DateTime?, int, int, ct)`; `IStablecoinGateway.GetMainWalletBalanceAsync(ct): Task<Money>`; `GetMasterAccountSummaryQuery`/`GetMasterAccountSummaryResult(Money MainWalletBalance, Money TotalSubAccountBalance, int SubAccountCount)`.

- [ ] **Step 1: Write the failing test for balance-enriched `ListSubAccountsQueryHandler`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/SubAccounts/ListSubAccountsQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.SubAccounts;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.SubAccounts;

public class ListSubAccountsQueryHandlerTests
{
    [Fact]
    public async Task Includes_current_balance_from_fund_account_when_present()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        var fundAccounts = Substitute.For<IFundAccountRepository>();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", CircleWalletId = "wallet-1",
            ComplianceState = SubAccountComplianceState.Accepted, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.ListAsync(null, Arg.Any<CancellationToken>()).Returns(new[] { subAccount });
        fundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>())
            .Returns(new FundAccount { Id = Guid.NewGuid(), ClientCompanyId = "acme-co", CurrencyCode = "USDC", Balance = 250m, UpdatedAtUtc = DateTime.UtcNow });

        var handler = new ListSubAccountsQueryHandler(subAccounts, fundAccounts);
        var result = await handler.HandleAsync(new ListSubAccountsQuery(null), TestContext.Current.CancellationToken);

        Assert.Equal(250m, result.Single().CurrentBalance!.Amount);
        Assert.Equal("USDC", result.Single().CurrentBalance!.CurrencyCode);
    }

    [Fact]
    public async Task CurrentBalance_is_null_when_no_fund_account_exists_yet()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        var fundAccounts = Substitute.For<IFundAccountRepository>();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", ComplianceState = SubAccountComplianceState.Pending,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.ListAsync(null, Arg.Any<CancellationToken>()).Returns(new[] { subAccount });
        fundAccounts.GetByClientCompanyIdAsync("acme-co", Arg.Any<CancellationToken>()).Returns((FundAccount?)null);

        var handler = new ListSubAccountsQueryHandler(subAccounts, fundAccounts);
        var result = await handler.HandleAsync(new ListSubAccountsQuery(null), TestContext.Current.CancellationToken);

        Assert.Null(result.Single().CurrentBalance);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListSubAccountsQueryHandlerTests"`
Expected: FAIL — build error, `ListSubAccountsQueryHandler` has no `IFundAccountRepository` constructor parameter and `GetSubAccountResult` has no `CurrentBalance`.

- [ ] **Step 3: Add `CurrentBalance` to `GetSubAccountResult` and populate it in `ListSubAccountsQueryHandler`**

Replace `src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountResult.cs` with:

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/GetSubAccountResult.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed record GetSubAccountResult(
    Guid SubAccountId,
    string ClientCompanyId,
    SubAccountLifecycleState LifecycleState,
    bool IsDisabled,
    string? CircleWalletId,
    EntityRegistrationStatus? LatestRegistrationStatus,
    string? RejectionReason,
    Money? CurrentBalance = null);
```

The new parameter defaults to `null` so `GetSubAccountQueryHandler`'s existing single-tenant `Get` call site (already covered by `BalancesController` for that tenant) needs no change.

Replace `src/TreasuryServiceOrchestrator.Application/SubAccounts/ListSubAccountsQueryHandler.cs` with:

```csharp
// src/TreasuryServiceOrchestrator.Application/SubAccounts/ListSubAccountsQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Compliance.Ports;

namespace TreasuryServiceOrchestrator.Application.SubAccounts;

public sealed class ListSubAccountsQueryHandler(ISubAccountRepository subAccounts, IFundAccountRepository fundAccounts)
    : IQueryHandler<ListSubAccountsQuery, IReadOnlyList<GetSubAccountResult>>
{
    public async Task<IReadOnlyList<GetSubAccountResult>> HandleAsync(
        ListSubAccountsQuery query, CancellationToken cancellationToken = default)
    {
        var all = await subAccounts.ListAsync(query.StateFilter, cancellationToken);
        var results = new List<GetSubAccountResult>(all.Count);

        foreach (var s in all)
        {
            var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(s.ClientCompanyId, cancellationToken);
            results.Add(new GetSubAccountResult(
                s.Id, s.ClientCompanyId, s.LifecycleState, s.IsDisabled, s.CircleWalletId, null, null,
                fundAccount is null ? null : new Domain.Money(fundAccount.Balance, fundAccount.CurrencyCode)));
        }

        return results;
    }
}
```

One `GetByClientCompanyIdAsync` call per row — acceptable at Phase 1's demo/mock data volume; batch this into a single query if the admin sub-account list ever needs to scale past a page.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*ListSubAccountsQueryHandlerTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Extend `ITransactionRepository` with `ListAllAsync` and add `ListAllTransactionsQuery`**

Append to `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/ITransactionRepository.cs` (inside the existing interface):

```csharp
    Task<IReadOnlyList<Transaction>> ListAllAsync(
        string? clientCompanyId,
        TransactionType? type,
        TransactionStatus? status,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/ListAllTransactionsQuery.cs
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record ListAllTransactionsQuery(
    string? ClientCompanyId, TransactionType? Type, TransactionStatus? Status,
    DateTime? FromUtc, DateTime? ToUtc, int Page, int PageSize);

public sealed class ListAllTransactionsQueryHandler(ITransactionRepository transactions)
    : IQueryHandler<ListAllTransactionsQuery, IReadOnlyList<Transaction>>
{
    public async Task<IReadOnlyList<Transaction>> HandleAsync(
        ListAllTransactionsQuery query, CancellationToken cancellationToken = default) =>
        await transactions.ListAllAsync(
            query.ClientCompanyId, query.Type, query.Status, query.FromUtc, query.ToUtc,
            query.Page, query.PageSize, cancellationToken);
}
```

`Transaction` carries no `HasQueryFilter` in `OnModelCreating` (unlike `RedeemRequest`) — Task 8 never scoped it with an EF global filter, so no `IgnoreQueryFilters()` is needed here; the tenant boundary for the tenant-facing `TransactionsController` (Task 8) is enforced entirely by `TenantScopeResolver` at the handler layer, same as this new admin path.

- [ ] **Step 6: Implement `TransactionRepository.ListAllAsync`**

Append to `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TransactionRepository.cs` (inside the existing class):

```csharp
    public async Task<IReadOnlyList<Transaction>> ListAllAsync(
        string? clientCompanyId, TransactionType? type, TransactionStatus? status,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Transactions.AsQueryable();

        if (clientCompanyId is not null)
        {
            query = query.Where(t => t.ClientCompanyId == clientCompanyId);
        }
        if (type is not null)
        {
            query = query.Where(t => t.Type == type);
        }
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }
        if (fromUtc is not null)
        {
            query = query.Where(t => t.CreatedAtUtc >= fromUtc);
        }
        if (toUtc is not null)
        {
            query = query.Where(t => t.CreatedAtUtc <= toUtc);
        }

        return await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
```

- [ ] **Step 7: Add `GetMainWalletBalanceAsync` to `IStablecoinGateway`, `CircleMintGateway`, and `MockStablecoinGateway`**

Add to `src/TreasuryServiceOrchestrator.Application/Ledger/Ports/GatewayDtos.cs`: nothing new is needed — `Money` already crosses this boundary.

Modify `src/TreasuryServiceOrchestrator.Application/Shared/Ports/IStablecoinGateway.cs` — add:

```csharp
Task<Money> GetMainWalletBalanceAsync(CancellationToken cancellationToken);
```

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs` — add:

```csharp
public Task<Money> GetMainWalletBalanceAsync(CancellationToken cancellationToken) =>
    Task.FromResult(new Money(0m, "USDC"));
```

(Real Circle Mint primary-wallet balance query — `GET /v1/businessAccount/balances` without `walletId`, per PRD §9.3 — is deferred to Phase 3; `0` is an honest placeholder, not a guess at a real balance.)

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockProviderOptions.cs` — add:

```csharp
public decimal MainWalletBalanceAmount { get; set; } = 10_000m;
```

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Mocks/MockStablecoinGateway.cs` — add:

```csharp
    public Task<Money> GetMainWalletBalanceAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new Money(options.Value.MainWalletBalanceAmount, "USDC"));
```

- [ ] **Step 8: Write the failing test for `GetMasterAccountSummaryQueryHandler`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Application/Admin/GetMasterAccountSummaryQueryHandlerTests.cs
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Admin;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Application.Admin;

public class GetMasterAccountSummaryQueryHandlerTests
{
    [Fact]
    public async Task Sums_latest_snapshot_balance_across_all_sub_accounts_plus_main_wallet()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        var snapshots = Substitute.For<IBalanceSnapshotRepository>();
        var gateway = Substitute.For<IStablecoinGateway>();
        var subAccountOne = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", ComplianceState = SubAccountComplianceState.Accepted,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        var subAccountTwo = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "beta-co", BusinessName = "Beta Co",
            BusinessUniqueIdentifier = "beta-ein-1", ComplianceState = SubAccountComplianceState.Accepted,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.ListAsync(null, Arg.Any<CancellationToken>()).Returns(new[] { subAccountOne, subAccountTwo });
        snapshots.GetLatestAsync(subAccountOne.Id, Arg.Any<CancellationToken>())
            .Returns(new BalanceSnapshot
            {
                Id = Guid.NewGuid(), SubAccountId = subAccountOne.Id, ClientCompanyId = "acme-co",
                Balance = new Money(300m, "USDC"), Reason = BalanceSnapshotReason.PostMutation, CapturedAtUtc = DateTime.UtcNow,
            });
        snapshots.GetLatestAsync(subAccountTwo.Id, Arg.Any<CancellationToken>())
            .Returns(new BalanceSnapshot
            {
                Id = Guid.NewGuid(), SubAccountId = subAccountTwo.Id, ClientCompanyId = "beta-co",
                Balance = new Money(150m, "USDC"), Reason = BalanceSnapshotReason.PostMutation, CapturedAtUtc = DateTime.UtcNow,
            });
        gateway.GetMainWalletBalanceAsync(Arg.Any<CancellationToken>()).Returns(new Money(10_000m, "USDC"));

        var handler = new GetMasterAccountSummaryQueryHandler(subAccounts, snapshots, gateway);
        var result = await handler.HandleAsync(new GetMasterAccountSummaryQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(10_000m, result.MainWalletBalance.Amount);
        Assert.Equal(450m, result.TotalSubAccountBalance.Amount);
        Assert.Equal(2, result.SubAccountCount);
    }

    [Fact]
    public async Task Sub_account_with_no_snapshot_yet_contributes_zero()
    {
        var subAccounts = Substitute.For<ISubAccountRepository>();
        var snapshots = Substitute.For<IBalanceSnapshotRepository>();
        var gateway = Substitute.For<IStablecoinGateway>();
        var subAccount = new SubAccount
        {
            Id = Guid.NewGuid(), ClientCompanyId = "acme-co", BusinessName = "Acme Co",
            BusinessUniqueIdentifier = "acme-ein-1", ComplianceState = SubAccountComplianceState.Pending,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        subAccounts.ListAsync(null, Arg.Any<CancellationToken>()).Returns(new[] { subAccount });
        snapshots.GetLatestAsync(subAccount.Id, Arg.Any<CancellationToken>()).Returns((BalanceSnapshot?)null);
        gateway.GetMainWalletBalanceAsync(Arg.Any<CancellationToken>()).Returns(new Money(10_000m, "USDC"));

        var handler = new GetMasterAccountSummaryQueryHandler(subAccounts, snapshots, gateway);
        var result = await handler.HandleAsync(new GetMasterAccountSummaryQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(0m, result.TotalSubAccountBalance.Amount);
    }
}
```

- [ ] **Step 9: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*GetMasterAccountSummaryQueryHandlerTests"`
Expected: FAIL — build error, `TreasuryServiceOrchestrator.Application.Admin` namespace doesn't exist.

- [ ] **Step 10: Implement `GetMasterAccountSummaryQuery`/`Handler`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Admin/GetMasterAccountSummaryQuery.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed record GetMasterAccountSummaryQuery;

public sealed record GetMasterAccountSummaryResult(Money MainWalletBalance, Money TotalSubAccountBalance, int SubAccountCount);
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Admin/GetMasterAccountSummaryQueryHandler.cs
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed class GetMasterAccountSummaryQueryHandler(
    ISubAccountRepository subAccounts, IBalanceSnapshotRepository snapshots, IStablecoinGateway gateway)
    : IQueryHandler<GetMasterAccountSummaryQuery, GetMasterAccountSummaryResult>
{
    public async Task<GetMasterAccountSummaryResult> HandleAsync(
        GetMasterAccountSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var mainWalletBalance = await gateway.GetMainWalletBalanceAsync(cancellationToken);
        var all = await subAccounts.ListAsync(stateFilter: null, cancellationToken);

        var total = 0m;
        foreach (var subAccount in all)
        {
            var latest = await snapshots.GetLatestAsync(subAccount.Id, cancellationToken);
            total += latest?.Balance.Amount ?? 0m;
        }

        return new GetMasterAccountSummaryResult(mainWalletBalance, new Money(total, "USDC"), all.Count);
    }
}
```

- [ ] **Step 11: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*GetMasterAccountSummaryQueryHandlerTests"`
Expected: PASS (2 tests).

- [ ] **Step 12: Add `AdminTransactionsController` and `MasterAccountController`, wire DI**

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/AdminTransactionsController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/transactions")]
public sealed class AdminTransactionsController(
    ICallerContext caller, IQueryHandler<ListAllTransactionsQuery, IReadOnlyList<Transaction>> listAllHandler)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? clientCompanyId, [FromQuery] TransactionType? type, [FromQuery] TransactionStatus? status,
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
        {
            throw new TenantForbiddenException("Only Admin may list transactions across tenants.");
        }

        var effectivePage = page <= 0 ? 1 : page;
        var effectivePageSize = pageSize <= 0 ? 20 : pageSize;

        var result = await listAllHandler.HandleAsync(
            new ListAllTransactionsQuery(clientCompanyId, type, status, fromUtc, toUtc, effectivePage, effectivePageSize),
            cancellationToken);

        return Ok(result);
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/MasterAccountController.cs
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application;
using TreasuryServiceOrchestrator.Application.Admin;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/master-account")]
public sealed class MasterAccountController(
    ICallerContext caller, IQueryHandler<GetMasterAccountSummaryQuery, GetMasterAccountSummaryResult> summaryHandler)
    : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
        {
            throw new TenantForbiddenException("Only Admin may view the master-account summary.");
        }

        var result = await summaryHandler.HandleAsync(new GetMasterAccountSummaryQuery(), cancellationToken);
        return Ok(result);
    }
}
```

Modify `src/TreasuryServiceOrchestrator.Api/Program.cs` — add:

```csharp
builder.Services.AddScoped<IQueryHandler<ListAllTransactionsQuery, IReadOnlyList<Transaction>>, ListAllTransactionsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetMasterAccountSummaryQuery, GetMasterAccountSummaryResult>, GetMasterAccountSummaryQueryHandler>();
```

(`ListSubAccountsQueryHandler`'s registration is unchanged — same interface, new constructor parameter resolved automatically by DI.)

- [ ] **Step 13: Write the failing integration test, then run the full suite**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/AdminCrossTenantViewsTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class AdminCrossTenantViewsTests(SqlServerTestDatabaseFixture fixture)
{
    [Fact]
    public async Task Admin_sees_all_sub_accounts_with_balances_all_tenants_transactions_and_master_account_summary()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "Tenant";
            config["KnownClientCompanies:1:Id"] = "admin";
            config["KnownClientCompanies:1:Role"] = "Admin";
            config["MockProvider:Enabled"] = "true";
            config["MockProvider:WebhookDelayMilliseconds"] = "0";
            config["MockProvider:MainWalletBalanceAmount"] = "10000";
        });

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("ClientCompanyId", "admin");
        using var tenantClient = factory.CreateClient();
        tenantClient.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");

        await adminClient.PostAsJsonAsync("/api/v1/sub-accounts", new
        {
            TargetClientCompanyId = "acme-co", BusinessName = "Acme Co", BusinessUniqueIdentifier = "acme-ein-1",
            IdempotencyKey = "sub-acme-1",
        }, TestContext.Current.CancellationToken);

        var listResponse = await adminClient.GetAsync("/api/v1/sub-accounts", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var forbiddenResponse = await tenantClient.GetAsync("/api/v1/sub-accounts", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        var allTransactionsResponse = await adminClient.GetAsync("/api/v1/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, allTransactionsResponse.StatusCode);

        var tenantForbiddenTransactionsResponse = await tenantClient.GetAsync("/api/v1/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, tenantForbiddenTransactionsResponse.StatusCode);

        var summaryResponse = await adminClient.GetAsync("/api/v1/master-account/summary", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<MasterAccountSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(10_000m, summary!.MainWalletBalance.Amount);

        var tenantForbiddenSummaryResponse = await tenantClient.GetAsync("/api/v1/master-account/summary", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, tenantForbiddenSummaryResponse.StatusCode);
    }

    private sealed record MasterAccountSummaryResponse(MoneyResponse MainWalletBalance, MoneyResponse TotalSubAccountBalance, int SubAccountCount);
    private sealed record MoneyResponse(decimal Amount, string CurrencyCode);
}
```

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 14: Commit**

```bash
git add -A
git commit -m "feat: admin cross-tenant sub-account/transaction views and master-account summary"
```

---

## Task 13: Internal notifications outbox + dispatcher (PRD §10.1, §15.1 slice 11)

Adds the outbox pattern PRD §10.1 requires: a `NotificationOutboxEntry` row written in the **same database transaction** as the state change it announces, then a background HTTP dispatcher that POSTs it to a configured internal-service endpoint with bounded backoff — never lost, because the row survives a crash between the state change and the send. A stub receiver controller stands in for the real internal service in the demo (§15.1 requirement 6; DLQ/replay/delivery-observability are Phase 2, §15.2).

Per the PRD §15.1 demo script, the notification-worthy state changes are exactly five: entity decision (`Accepted`/`Rejected`), deposit credit, recipient approval decision, transfer completion, redemption completion. Only these five call sites are wired this task — PRD §10.1 requirement 2 says "all state changes", but every other mutating handler (create sub-account, register recipient, create transfer/redemption, create linked bank account) is an *API-initiated* action the caller already gets a synchronous response for; the acceptance script only exercises the five webhook-driven transitions above, so wiring more now would be speculative.

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Domain/NotificationDeliveryStatus.cs`
- Create: `src/TreasuryServiceOrchestrator.Domain/NotificationOutboxEntry.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/INotificationOutboxRepository.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/INotificationSender.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatcherOptions.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Notifications/HttpNotificationSender.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatcher.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatchBackgroundService.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/NotificationOutboxRepository.cs`
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/SubAccounts/ProcessExternalEntityDecisionHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommandHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Transfers/ProcessTransferStatusCommandHandler.cs`
- Modify: `src/TreasuryServiceOrchestrator.Application/Redemptions/ProcessPayoutStatusCommandHandler.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/InternalNotificationsStubController.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Middleware/ClientCompanyIdMiddleware.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.json`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Notifications/NotificationDispatcherTests.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/NotificationOutboxDeliveryTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork.SaveChangesAsync` (existing two-phase pattern — the outbox `AddAsync` call is added inside each handler before its existing `SaveChangesAsync`, so it lands in the same transaction), `TreasuryServiceOrchestratorDbContext` (Task 8).
- Produces: `NotificationOutboxEntry { Guid Id; string EventType; string ClientCompanyId; string EntityId; DateTime OccurredAtUtc; string CorrelationId; string PayloadJson; NotificationDeliveryStatus Status; int AttemptCount; DateTime? NextAttemptAtUtc; DateTime? DeliveredAtUtc; }`; `INotificationOutboxRepository`/`INotificationSender` (signatures below) — later phases (Phase 2 dead-letter/replay, §15.2) build on these without changing their shape.

- [ ] **Step 1: Add the domain types**

```csharp
// src/TreasuryServiceOrchestrator.Domain/NotificationDeliveryStatus.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum NotificationDeliveryStatus
{
    Pending,
    Delivered,
}
```

```csharp
// src/TreasuryServiceOrchestrator.Domain/NotificationOutboxEntry.cs
namespace TreasuryServiceOrchestrator.Domain;

public class NotificationOutboxEntry
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public required string ClientCompanyId { get; set; }
    public required string EntityId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public required string CorrelationId { get; set; }
    public required string PayloadJson { get; set; }
    public NotificationDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}
```

Delivery is at-least-once (PRD §10.1 requirement 5) — there is no `Failed`/dead-letter status in Phase 1; a row that keeps failing simply keeps its `NextAttemptAtUtc` pushed further out and stays `Pending` forever, per requirement 4 ("failed deliveries stay queued"). Phase 2 (§15.2) adds the dead-letter state.

- [ ] **Step 2: Add `INotificationOutboxRepository` and `INotificationSender`**

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/INotificationOutboxRepository.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface INotificationOutboxRepository
{
    Task AddAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationOutboxEntry>> GetDueBatchAsync(
        int batchSize, DateTime nowUtc, CancellationToken cancellationToken);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/INotificationSender.cs
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface INotificationSender
{
    Task<bool> SendAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write the failing test for `NotificationDispatcher`**

```csharp
// tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Notifications/NotificationDispatcherTests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Notifications;
using Xunit;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Notifications;

public class NotificationDispatcherTests
{
    private static NotificationDispatcher NewDispatcher(
        INotificationOutboxRepository outbox, INotificationSender sender, IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(outbox);
        services.AddSingleton(sender);
        services.AddSingleton(unitOfWork);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new NotificationDispatcherOptions
        {
            MaxBatchSize = 20, BaseBackoffMilliseconds = 1000, MaxBackoffMilliseconds = 60000,
        });
        return new NotificationDispatcher(scopeFactory, options);
    }

    [Fact]
    public async Task Marks_entry_Delivered_when_sender_succeeds()
    {
        var entry = new NotificationOutboxEntry
        {
            Id = Guid.NewGuid(), EventType = "DepositCredited", ClientCompanyId = "acme-co", EntityId = Guid.NewGuid().ToString(),
            OccurredAtUtc = DateTime.UtcNow, CorrelationId = "corr-1", PayloadJson = "{}", Status = NotificationDeliveryStatus.Pending,
        };
        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox.GetDueBatchAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new[] { entry });
        var sender = Substitute.For<INotificationSender>();
        sender.SendAsync(entry, Arg.Any<CancellationToken>()).Returns(true);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var dispatcher = NewDispatcher(outbox, sender, unitOfWork);
        var dispatchedCount = await dispatcher.DispatchDueBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, dispatchedCount);
        Assert.Equal(NotificationDeliveryStatus.Delivered, entry.Status);
        Assert.NotNull(entry.DeliveredAtUtc);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Schedules_backoff_and_stays_Pending_when_sender_fails()
    {
        var entry = new NotificationOutboxEntry
        {
            Id = Guid.NewGuid(), EventType = "DepositCredited", ClientCompanyId = "acme-co", EntityId = Guid.NewGuid().ToString(),
            OccurredAtUtc = DateTime.UtcNow, CorrelationId = "corr-1", PayloadJson = "{}", Status = NotificationDeliveryStatus.Pending,
        };
        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox.GetDueBatchAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new[] { entry });
        var sender = Substitute.For<INotificationSender>();
        sender.SendAsync(entry, Arg.Any<CancellationToken>()).Returns(false);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var dispatcher = NewDispatcher(outbox, sender, unitOfWork);
        await dispatcher.DispatchDueBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NotificationDeliveryStatus.Pending, entry.Status);
        Assert.Equal(1, entry.AttemptCount);
        Assert.True(entry.NextAttemptAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Does_not_call_SaveChangesAsync_when_no_entries_are_due()
    {
        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox.GetDueBatchAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<NotificationOutboxEntry>());
        var sender = Substitute.For<INotificationSender>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var dispatcher = NewDispatcher(outbox, sender, unitOfWork);
        var dispatchedCount = await dispatcher.DispatchDueBatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, dispatchedCount);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*NotificationDispatcherTests"`
Expected: FAIL — build error, `TreasuryServiceOrchestrator.Infrastructure.Notifications` namespace doesn't exist.

- [ ] **Step 5: Implement `NotificationDispatcherOptions`, `HttpNotificationSender`, `NotificationDispatcher`, `NotificationDispatchBackgroundService`**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatcherOptions.cs
namespace TreasuryServiceOrchestrator.Infrastructure.Notifications;

public sealed class NotificationDispatcherOptions
{
    public string EndpointUrl { get; set; } = "http://localhost:5080/internal/notifications";
    public string? AuthHeaderName { get; set; }
    public string? AuthHeaderValue { get; set; }
    public int MaxBatchSize { get; set; } = 20;
    public int PollingIntervalMilliseconds { get; set; } = 500;
    public int BaseBackoffMilliseconds { get; set; } = 1000;
    public int MaxBackoffMilliseconds { get; set; } = 60000;
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Notifications/HttpNotificationSender.cs
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Notifications;

public sealed class HttpNotificationSender(HttpClient httpClient, IOptions<NotificationDispatcherOptions> options)
    : INotificationSender
{
    public async Task<bool> SendAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.EndpointUrl)
        {
            Content = JsonContent.Create(new
            {
                eventId = entry.Id,
                eventType = entry.EventType,
                clientCompanyId = entry.ClientCompanyId,
                entityId = entry.EntityId,
                occurredAtUtc = entry.OccurredAtUtc,
                correlationId = entry.CorrelationId,
                payload = JsonSerializer.Deserialize<JsonElement>(entry.PayloadJson),
            }),
        };

        if (settings.AuthHeaderName is not null)
        {
            request.Headers.TryAddWithoutValidation(settings.AuthHeaderName, settings.AuthHeaderValue);
        }

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatcher.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Notifications;

public sealed class NotificationDispatcher(IServiceScopeFactory scopeFactory, IOptions<NotificationDispatcherOptions> options)
{
    public async Task<int> DispatchDueBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var settings = options.Value;

        var due = await outbox.GetDueBatchAsync(settings.MaxBatchSize, DateTime.UtcNow, cancellationToken);

        foreach (var entry in due)
        {
            var delivered = await sender.SendAsync(entry, cancellationToken);
            if (delivered)
            {
                entry.Status = Domain.NotificationDeliveryStatus.Delivered;
                entry.DeliveredAtUtc = DateTime.UtcNow;
            }
            else
            {
                entry.AttemptCount++;
                var backoffMilliseconds = Math.Min(
                    settings.BaseBackoffMilliseconds * (1 << Math.Min(entry.AttemptCount, 10)),
                    settings.MaxBackoffMilliseconds);
                entry.NextAttemptAtUtc = DateTime.UtcNow.AddMilliseconds(backoffMilliseconds);
            }
        }

        if (due.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }
}
```

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatchBackgroundService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TreasuryServiceOrchestrator.Infrastructure.Notifications;

public sealed class NotificationDispatchBackgroundService(
    NotificationDispatcher dispatcher, IOptions<NotificationDispatcherOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(options.Value.PollingIntervalMilliseconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await dispatcher.DispatchDueBatchAsync(stoppingToken);
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on host shutdown.
        }
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/TreasuryServiceOrchestrator.UnitTests -- --filter-class "*NotificationDispatcherTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Implement `NotificationOutboxRepository` and its `DbContext` mapping**

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Persistence/NotificationOutboxRepository.cs
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Persistence;

public sealed class NotificationOutboxRepository(TreasuryServiceOrchestratorDbContext dbContext) : INotificationOutboxRepository
{
    public async Task AddAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken)
    {
        await dbContext.NotificationOutboxEntries.AddAsync(entry, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationOutboxEntry>> GetDueBatchAsync(
        int batchSize, DateTime nowUtc, CancellationToken cancellationToken) =>
        await dbContext.NotificationOutboxEntries
            .Where(e => e.Status == NotificationDeliveryStatus.Pending && (e.NextAttemptAtUtc == null || e.NextAttemptAtUtc <= nowUtc))
            .OrderBy(e => e.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
}
```

Modify `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs` — add the DbSet:

```csharp
public DbSet<NotificationOutboxEntry> NotificationOutboxEntries => Set<NotificationOutboxEntry>();
```

and, inside `OnModelCreating`, add:

```csharp
modelBuilder.Entity<NotificationOutboxEntry>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ClientCompanyId).HasMaxLength(450).UseCollation(ClientCompanyIdCollation);
    entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc });
});
```

Regenerate the migration the same way every prior task has:

```bash
dotnet ef migrations remove --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --force
dotnet ef migrations add InitialCreate --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/TreasuryServiceOrchestrator.Infrastructure --startup-project src/TreasuryServiceOrchestrator.Api
```
Expected: a regenerated `InitialCreate` with a `NotificationOutboxEntries` table, then `No changes`.

- [ ] **Step 8: Wire the outbox write into the entity-decision handler**

Modify `src/TreasuryServiceOrchestrator.Application/SubAccounts/ProcessExternalEntityDecisionHandler.cs` — add `INotificationOutboxRepository outbox` as a constructor parameter, and inside the `if (newStatus is EntityRegistrationStatus.Accepted or EntityRegistrationStatus.Rejected)` block (right after `subAccount.UpdatedAtUtc = DateTime.UtcNow;`), add:

```csharp
            await outbox.AddAsync(new NotificationOutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "EntityRegistrationDecided",
                ClientCompanyId = subAccount.ClientCompanyId,
                EntityId = subAccount.Id.ToString(),
                OccurredAtUtc = subAccount.UpdatedAtUtc,
                CorrelationId = command.WalletId,
                PayloadJson = JsonSerializer.Serialize(new { subAccount.LifecycleState, RegistrationStatus = newStatus }),
                Status = NotificationDeliveryStatus.Pending,
            }, cancellationToken);
```

- [ ] **Step 9: Wire the outbox write into the deposit-credit handler**

Modify `src/TreasuryServiceOrchestrator.Application/Ledger/ProcessDepositCommandHandler.cs` — add `INotificationOutboxRepository outbox` as a constructor parameter, and inside `RecordCompleteAsync`, right before its `return new ProcessDepositResult(...)`, add:

```csharp
        await outbox.AddAsync(new NotificationOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = "DepositCredited",
            ClientCompanyId = subAccount.ClientCompanyId,
            EntityId = transaction.Id.ToString(),
            OccurredAtUtc = transaction.UpdatedAtUtc,
            CorrelationId = command.CorrelationId,
            PayloadJson = JsonSerializer.Serialize(new { transaction.Amount, fundAccount.Balance }),
            Status = NotificationDeliveryStatus.Pending,
        }, cancellationToken);
```

(Only `RecordCompleteAsync` gets a notification — `RecordFailedAsync`'s currency-mismatch path is not one of the demo script's five events.)

- [ ] **Step 10: Wire the outbox write into the recipient-decision handler**

Modify `src/TreasuryServiceOrchestrator.Application/Recipients/ProcessRecipientDecisionHandler.cs` — add `INotificationOutboxRepository outbox` as a constructor parameter, and right before `await unitOfWork.SaveChangesAsync(cancellationToken);`, add:

```csharp
        await outbox.AddAsync(new NotificationOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = "RecipientApprovalDecided",
            ClientCompanyId = recipient.ClientCompanyId,
            EntityId = recipient.Id.ToString(),
            OccurredAtUtc = recipient.UpdatedAtUtc,
            CorrelationId = command.CircleRecipientId,
            PayloadJson = JsonSerializer.Serialize(new { recipient.Status, recipient.DenialReason }),
            Status = NotificationDeliveryStatus.Pending,
        }, cancellationToken);
```

- [ ] **Step 11: Wire the outbox write into the transfer-completion handler**

Modify `src/TreasuryServiceOrchestrator.Application/Transfers/ProcessTransferStatusCommandHandler.cs` — add `INotificationOutboxRepository outbox` as a constructor parameter, and inside the `if (newStatus == TransferStatus.Complete)` block, right after the `snapshots.AddAsync(...)` call, add:

```csharp
            await outbox.AddAsync(new NotificationOutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "TransferCompleted",
                ClientCompanyId = transfer.ClientCompanyId,
                EntityId = transfer.Id.ToString(),
                OccurredAtUtc = transfer.UpdatedAtUtc,
                CorrelationId = command.CircleTransferId,
                PayloadJson = JsonSerializer.Serialize(new { transfer.Amount, transfer.Status }),
                Status = NotificationDeliveryStatus.Pending,
            }, cancellationToken);
```

- [ ] **Step 12: Wire the outbox write into the redemption-completion handler**

Modify `src/TreasuryServiceOrchestrator.Application/Redemptions/ProcessPayoutStatusCommandHandler.cs` — add `INotificationOutboxRepository outbox` as a constructor parameter, and inside the `if (newStatus == TransferStatus.Complete)` block, right after the `snapshots.AddAsync(...)` call, add:

```csharp
            await outbox.AddAsync(new NotificationOutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "RedemptionCompleted",
                ClientCompanyId = redeemRequest.ClientCompanyId,
                EntityId = redeemRequest.Id.ToString(),
                OccurredAtUtc = redeemRequest.UpdatedAtUtc,
                CorrelationId = command.CircleRedeemId,
                PayloadJson = JsonSerializer.Serialize(new { redeemRequest.GrossAmount, redeemRequest.Fees, redeemRequest.NetAmount }),
                Status = NotificationDeliveryStatus.Pending,
            }, cancellationToken);
```

All five sites add the `outbox.AddAsync` call **before** the handler's existing (unchanged) `unitOfWork.SaveChangesAsync(cancellationToken)` — that single `SaveChangesAsync` is what makes the state change and the outbox row atomic, satisfying PRD §10.1 requirement 4 with no new transaction management.

- [ ] **Step 13: Add the stub receiver controller and the middleware bypass**

```csharp
// src/TreasuryServiceOrchestrator.Api/Controllers/InternalNotificationsStubController.cs
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace TreasuryServiceOrchestrator.Api.Controllers;

[ApiController]
[Route("internal/notifications")]
public sealed class InternalNotificationsStubController(ILogger<InternalNotificationsStubController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult Receive([FromBody] JsonElement payload)
    {
        logger.LogInformation("Stub internal-notification receiver got event: {Payload}", payload.GetRawText());
        return Ok();
    }
}
```

This controller carries no `[ApiVersion]`/`api/v{version:apiVersion}` prefix — it stands in for an **external** internal service, not a versioned resource of this API.

Modify `src/TreasuryServiceOrchestrator.Api/Middleware/ClientCompanyIdMiddleware.cs` — extend the bypass list:

```csharp
private static readonly string[] BypassPaths = ["/health/live", "/health/ready", "/api/v1/webhooks/circle", "/internal/notifications"];
```

The stub receiver is not a registered `ClientCompanyId` caller — same reasoning as the Circle webhook endpoint bypass (authenticated by a different mechanism entirely; the real internal service will carry its own shared-secret auth in Phase 2, out of scope here).

- [ ] **Step 14: Wire DI and configuration**

Modify `src/TreasuryServiceOrchestrator.Api/Program.cs` — add:

```csharp
builder.Services.Configure<NotificationDispatcherOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.AddHttpClient<INotificationSender, HttpNotificationSender>();
builder.Services.AddScoped<INotificationOutboxRepository, NotificationOutboxRepository>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddHostedService<NotificationDispatchBackgroundService>();
```

Modify `src/TreasuryServiceOrchestrator.Api/appsettings.json` — add:

```json
"Notifications": {
  "EndpointUrl": "http://localhost:5080/internal/notifications",
  "MaxBatchSize": 20,
  "PollingIntervalMilliseconds": 500,
  "BaseBackoffMilliseconds": 1000,
  "MaxBackoffMilliseconds": 60000
}
```

`AddHttpClient<INotificationSender, HttpNotificationSender>()` registers `HttpNotificationSender` scoped (matching the other five handlers' scoped lifetime) with a managed `HttpClient` — `NotificationDispatcher` resolves it per-batch through its own `IServiceScopeFactory` scope (Step 5), so it never holds a long-lived `HttpClient` across scopes.

- [ ] **Step 15: Write the failing integration test, then run the full suite**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/NotificationOutboxDeliveryTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class NotificationOutboxDeliveryTests(SqlServerTestDatabaseFixture fixture)
{
    [Fact]
    public async Task Deposit_credit_produces_a_delivered_internal_notification()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = "acme-co";
            config["KnownClientCompanies:0:Role"] = "Tenant";
            config["KnownClientCompanies:1:Id"] = "admin";
            config["KnownClientCompanies:1:Role"] = "Admin";
            config["MockProvider:Enabled"] = "true";
            config["MockProvider:WebhookDelayMilliseconds"] = "0";
            config["Notifications:PollingIntervalMilliseconds"] = "50";
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", "acme-co");
        var baseAddress = client.BaseAddress!.ToString().TrimEnd('/');
        factory.OverrideConfig(config => config["Notifications:EndpointUrl"] = $"{baseAddress}/internal/notifications");

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("ClientCompanyId", "admin");
        await adminClient.PostAsJsonAsync("/api/v1/sub-accounts", new
        {
            TargetClientCompanyId = "acme-co", BusinessName = "Acme Co", BusinessUniqueIdentifier = "acme-ein-1",
            IdempotencyKey = "sub-acme-1",
        }, TestContext.Current.CancellationToken);

        DepositAddressResponse? depositAddress = null;
        for (var attempt = 0; attempt < 10 && depositAddress is null; attempt++)
        {
            var addressResponse = await client.PostAsJsonAsync(
                "/api/v1/sub-accounts/acme-co/deposit-addresses", new { Chain = "ETH", CurrencyCode = "USDC" },
                TestContext.Current.CancellationToken);
            if (addressResponse.IsSuccessStatusCode)
            {
                depositAddress = await addressResponse.Content.ReadFromJsonAsync<DepositAddressResponse>(TestContext.Current.CancellationToken);
            }
            else
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        Assert.NotNull(depositAddress);

        var transaction = await PollForCompletedDepositAsync(client, TestContext.Current.CancellationToken);
        Assert.Equal("Complete", transaction.Status);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestrator.Infrastructure.Persistence.TreasuryServiceOrchestratorDbContext>();

        NotificationOutboxEntry? delivered = null;
        for (var attempt = 0; attempt < 20 && delivered is null; attempt++)
        {
            delivered = dbContext.NotificationOutboxEntries.AsNoTracking()
                .SingleOrDefault(e => e.EventType == "DepositCredited" && e.Status == NotificationDeliveryStatus.Delivered);
            if (delivered is null)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        }

        Assert.NotNull(delivered);
    }

    private static async Task<TransactionResponse> PollForCompletedDepositAsync(HttpClient client, CancellationToken cancellationToken)
    {
        TransactionResponse? transaction = null;
        for (var attempt = 0; attempt < 20 && transaction?.Status != "Complete"; attempt++)
        {
            var listResponse = await client.GetFromJsonAsync<List<TransactionResponse>>(
                "/api/v1/sub-accounts/acme-co/transactions", cancellationToken);
            transaction = listResponse?.FirstOrDefault(t => t.Type == "Deposit");
            if (transaction?.Status != "Complete")
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return transaction ?? throw new InvalidOperationException("Deposit never completed.");
    }

    private sealed record DepositAddressResponse(Guid DepositAddressId, string Chain, string CurrencyCode, string Address);
    private sealed record TransactionResponse(Guid Id, string Type, string Status);
}
```

This test relies on `TreasuryServiceOrchestratorWebApplicationFactory.OverrideConfig` (an existing test-support hook already used to inject config after the factory is built, since the stub receiver's URL is only known once the in-memory test server has assigned itself a `BaseAddress`) and on the mock deposit-crediting flow already wired end-to-end by Task 7/8's deposit-address-generation-then-simulated-webhook path.

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green.

- [ ] **Step 16: Commit**

```bash
git add -A
git commit -m "feat: internal notifications outbox + HTTP dispatcher, stub receiver endpoint"
```

---

## Task 14: Demo-script end-to-end integration test (PRD §15.1)

Single integration test that walks the PRD §15.1 demo script start to finish, asserting every clause of the literal acceptance sentence: "admin creates a sub-account for a client company → screening comes back `ACCEPTED` (and a second one `REJECTED` + resubmitted) → generate deposit address → simulated deposit credits the ledger and balance rises → register a recipient, simulated approval → outbound transfer completes → redemption completes showing gross/fees/net → tenant sees only its own data; admin sees all sub-accounts and the master summary → every step visible in transactions, balance history, and audit records — and each state change ... also arrives as an internal-notification callback at the stub receiver." If this test is green, Phase 1 is done; if it is red, Phase 1 is not done — there is no separate acceptance step beyond this file.

**Files:**
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/DemoScriptEndToEndTests.cs`

**Interfaces:**
- Consumes: every controller route and DTO shape established in Tasks 1-13 (`SubAccountsController`, `LinkedBankAccountsController`, deposit-address/transaction/recipient/transfer/redemption endpoints, `AdminTransactionsController`, `MasterAccountController`, the `internal/notifications` stub receiver, `NotificationOutboxEntries` table).
- Produces: nothing consumed by later tasks — this is the terminal acceptance gate.

- [ ] **Step 1: Write the end-to-end test**

```csharp
// tests/TreasuryServiceOrchestrator.IntegrationTests/DemoScriptEndToEndTests.cs
using System.Net;
using System.Net.Http.Json;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;
using TreasuryServiceOrchestrator.IntegrationTests.Support;
using Xunit;

namespace TreasuryServiceOrchestrator.IntegrationTests;

[Collection("HostBuilding")]
public class DemoScriptEndToEndTests(SqlServerTestDatabaseFixture fixture)
{
    private const string AcceptedTenant = "acme-co";
    private const string RejectedTenant = "beta-co";

    [Fact]
    public async Task Demo_script_runs_start_to_finish_per_PRD_15_1()
    {
        await using var factory = new TreasuryServiceOrchestratorWebApplicationFactory(fixture.ConnectionString, config =>
        {
            config["KnownClientCompanies:0:Id"] = AcceptedTenant;
            config["KnownClientCompanies:0:Role"] = "Tenant";
            config["KnownClientCompanies:1:Id"] = RejectedTenant;
            config["KnownClientCompanies:1:Role"] = "Tenant";
            config["KnownClientCompanies:2:Id"] = "admin";
            config["KnownClientCompanies:2:Role"] = "Admin";
            config["MockProvider:Enabled"] = "true";
            config["MockProvider:WebhookDelayMilliseconds"] = "0";
            config["Notifications:PollingIntervalMilliseconds"] = "50";
        });

        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("ClientCompanyId", "admin");
        using var acme = factory.CreateClient();
        acme.DefaultRequestHeaders.Add("ClientCompanyId", AcceptedTenant);
        using var beta = factory.CreateClient();
        beta.DefaultRequestHeaders.Add("ClientCompanyId", RejectedTenant);

        var baseAddress = admin.BaseAddress!.ToString().TrimEnd('/');
        factory.OverrideConfig(config => config["Notifications:EndpointUrl"] = $"{baseAddress}/internal/notifications");

        // Step 1: admin creates a sub-account for a client company -> ACCEPTED
        var acmeCreate = await admin.PostAsJsonAsync("/api/v1/sub-accounts", new
        {
            TargetClientCompanyId = AcceptedTenant, BusinessName = "Acme Co", BusinessUniqueIdentifier = "acme-ein-1",
            IdempotencyKey = "sub-acme-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, acmeCreate.StatusCode);
        var acmeSubAccount = await PollUntilComplianceStateAsync(admin, AcceptedTenant, "Accepted", TestContext.Current.CancellationToken);
        Assert.Equal("Accepted", acmeSubAccount.ComplianceState);

        // Step 2: a second sub-account comes back REJECTED, then is resubmitted -> ACCEPTED
        var betaCreate = await admin.PostAsJsonAsync("/api/v1/sub-accounts", new
        {
            TargetClientCompanyId = RejectedTenant, BusinessName = "Beta Co", BusinessUniqueIdentifier = "REJECT-ME",
            IdempotencyKey = "sub-beta-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, betaCreate.StatusCode);
        var betaSubAccount = await PollUntilComplianceStateAsync(admin, RejectedTenant, "Rejected", TestContext.Current.CancellationToken);
        Assert.Equal("Rejected", betaSubAccount.ComplianceState);

        var resubmit = await admin.PostAsJsonAsync(
            $"/api/v1/sub-accounts/{RejectedTenant}/resubmit",
            new { BusinessUniqueIdentifier = "beta-ein-1", IdempotencyKey = "sub-beta-resubmit-1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        betaSubAccount = await PollUntilComplianceStateAsync(admin, RejectedTenant, "Accepted", TestContext.Current.CancellationToken);
        Assert.Equal("Accepted", betaSubAccount.ComplianceState);

        // Step 3: generate deposit address -> simulated deposit credits the ledger, balance rises
        var depositAddressResponse = await acme.PostAsJsonAsync(
            $"/api/v1/sub-accounts/{AcceptedTenant}/deposit-addresses", new { Chain = "ETH", CurrencyCode = "USDC" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, depositAddressResponse.StatusCode);

        var depositTransaction = await PollForTransactionAsync(acme, AcceptedTenant, "Deposit", "Complete", TestContext.Current.CancellationToken);
        Assert.Equal("Complete", depositTransaction.Status);

        var balanceAfterDeposit = await acme.GetFromJsonAsync<BalanceResponse>(
            $"/api/v1/sub-accounts/{AcceptedTenant}/balance", TestContext.Current.CancellationToken);
        Assert.NotNull(balanceAfterDeposit);
        Assert.True(balanceAfterDeposit!.Amount > 0m);

        // Step 4: register a recipient, simulated approval
        var recipientResponse = await acme.PostAsJsonAsync($"/api/v1/sub-accounts/{AcceptedTenant}/recipients", new
        {
            Chain = "ETH", Address = "0xRecipientAddress1", Nickname = "Vendor Payout", IdempotencyKey = "recipient-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, recipientResponse.StatusCode);
        var recipient = await recipientResponse.Content.ReadFromJsonAsync<RecipientResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(recipient);
        var approvedRecipient = await PollForRecipientStatusAsync(acme, AcceptedTenant, recipient!.Id, "Approved", TestContext.Current.CancellationToken);
        Assert.Equal("Approved", approvedRecipient.Status);

        // Step 5: outbound transfer completes
        var transferResponse = await acme.PostAsJsonAsync($"/api/v1/sub-accounts/{AcceptedTenant}/transfers", new
        {
            RecipientId = recipient.Id, Amount = 10m, CurrencyCode = "USDC", IdempotencyKey = "transfer-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var transferTransaction = await PollForTransactionAsync(acme, AcceptedTenant, "Transfer", "Complete", TestContext.Current.CancellationToken);
        Assert.Equal("Complete", transferTransaction.Status);

        // Step 6: redemption completes showing gross/fees/net
        var linkedBankAccountResponse = await admin.PostAsJsonAsync("/api/v1/linked-bank-accounts", new
        {
            BeneficiaryName = "Acme Co", AccountNumber = "000123456789", RoutingNumber = "021000021", BankName = "Test Bank",
            IdempotencyKey = "linked-bank-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, linkedBankAccountResponse.StatusCode);
        var linkedBankAccount = await linkedBankAccountResponse.Content.ReadFromJsonAsync<LinkedBankAccountResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(linkedBankAccount);

        var redemptionResponse = await acme.PostAsJsonAsync($"/api/v1/sub-accounts/{AcceptedTenant}/redemptions", new
        {
            LinkedBankAccountId = linkedBankAccount!.Id, GrossAmount = 5m, IdempotencyKey = "redemption-1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, redemptionResponse.StatusCode);
        var redemption = await PollForRedemptionCompletionAsync(acme, AcceptedTenant, TestContext.Current.CancellationToken);
        Assert.Equal("Complete", redemption.Status);
        Assert.NotNull(redemption.Fees);
        Assert.NotNull(redemption.NetAmount);
        Assert.Equal(redemption.GrossAmount - redemption.Fees!.Value, redemption.NetAmount!.Value);

        // Step 7: tenant sees only its own data
        var acmeTransactions = await acme.GetFromJsonAsync<List<TransactionResponse>>(
            $"/api/v1/sub-accounts/{AcceptedTenant}/transactions", TestContext.Current.CancellationToken);
        Assert.NotNull(acmeTransactions);
        Assert.DoesNotContain(acmeTransactions!, t => t.ClientCompanyId != AcceptedTenant);

        var betaForbidden = await beta.GetAsync($"/api/v1/sub-accounts/{AcceptedTenant}/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, betaForbidden.StatusCode);

        // Step 8: admin sees all sub-accounts and the master summary
        var adminSubAccounts = await admin.GetFromJsonAsync<List<SubAccountResponse>>("/api/v1/sub-accounts", TestContext.Current.CancellationToken);
        Assert.NotNull(adminSubAccounts);
        Assert.Contains(adminSubAccounts!, s => s.ClientCompanyId == AcceptedTenant);
        Assert.Contains(adminSubAccounts!, s => s.ClientCompanyId == RejectedTenant);

        var adminTransactions = await admin.GetFromJsonAsync<List<TransactionResponse>>("/api/v1/transactions", TestContext.Current.CancellationToken);
        Assert.NotNull(adminTransactions);
        Assert.Contains(adminTransactions!, t => t.ClientCompanyId == AcceptedTenant);

        var masterSummary = await admin.GetFromJsonAsync<MasterAccountSummaryResponse>(
            "/api/v1/master-account/summary", TestContext.Current.CancellationToken);
        Assert.NotNull(masterSummary);
        Assert.True(masterSummary!.MainWalletBalance.Amount > 0m);
        Assert.True(masterSummary.SubAccountCount >= 2);

        // Step 9: every step visible in balance history and audit records
        var balanceHistory = await acme.GetFromJsonAsync<List<BalanceSnapshotResponse>>(
            $"/api/v1/sub-accounts/{AcceptedTenant}/balance-history", TestContext.Current.CancellationToken);
        Assert.NotNull(balanceHistory);
        Assert.True(balanceHistory!.Count >= 2);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var auditEventTypes = new[]
        {
            "SubAccountComplianceDecision", "DepositCredited", "RecipientApprovalDecision", "TransferStatusChanged", "RedemptionStatusChanged",
        };
        foreach (var eventType in auditEventTypes)
        {
            Assert.True(
                dbContext.AuditLogEntries.AsNoTracking().Any(e => e.Action == eventType),
                $"expected an audit record for action '{eventType}'");
        }

        // Step 10: every one of the five notification-worthy state changes reached the stub receiver
        var expectedNotificationEventTypes = new[]
        {
            "EntityRegistrationDecided", "DepositCredited", "RecipientApprovalDecided", "TransferCompleted", "RedemptionCompleted",
        };
        foreach (var eventType in expectedNotificationEventTypes)
        {
            var delivered = await PollForDeliveredNotificationAsync(dbContext, eventType, TestContext.Current.CancellationToken);
            Assert.True(delivered, $"expected a Delivered notification outbox entry for event type '{eventType}'");
        }
    }

    private static async Task<SubAccountResponse> PollUntilComplianceStateAsync(
        HttpClient admin, string clientCompanyId, string expectedState, CancellationToken cancellationToken)
    {
        SubAccountResponse? subAccount = null;
        for (var attempt = 0; attempt < 20 && subAccount?.ComplianceState != expectedState; attempt++)
        {
            subAccount = await admin.GetFromJsonAsync<SubAccountResponse>($"/api/v1/sub-accounts/{clientCompanyId}", cancellationToken);
            if (subAccount?.ComplianceState != expectedState)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return subAccount ?? throw new InvalidOperationException($"sub-account for '{clientCompanyId}' never reached '{expectedState}'.");
    }

    private static async Task<TransactionResponse> PollForTransactionAsync(
        HttpClient client, string clientCompanyId, string type, string expectedStatus, CancellationToken cancellationToken)
    {
        TransactionResponse? transaction = null;
        for (var attempt = 0; attempt < 20 && transaction?.Status != expectedStatus; attempt++)
        {
            var transactions = await client.GetFromJsonAsync<List<TransactionResponse>>(
                $"/api/v1/sub-accounts/{clientCompanyId}/transactions", cancellationToken);
            transaction = transactions?.FirstOrDefault(t => t.Type == type);
            if (transaction?.Status != expectedStatus)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return transaction ?? throw new InvalidOperationException($"transaction of type '{type}' never reached '{expectedStatus}'.");
    }

    private static async Task<RecipientResponse> PollForRecipientStatusAsync(
        HttpClient client, string clientCompanyId, Guid recipientId, string expectedStatus, CancellationToken cancellationToken)
    {
        RecipientResponse? recipient = null;
        for (var attempt = 0; attempt < 20 && recipient?.Status != expectedStatus; attempt++)
        {
            recipient = await client.GetFromJsonAsync<RecipientResponse>(
                $"/api/v1/sub-accounts/{clientCompanyId}/recipients/{recipientId}", cancellationToken);
            if (recipient?.Status != expectedStatus)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return recipient ?? throw new InvalidOperationException("recipient never reached expected status.");
    }

    private static async Task<RedemptionResponse> PollForRedemptionCompletionAsync(
        HttpClient client, string clientCompanyId, CancellationToken cancellationToken)
    {
        RedemptionResponse? redemption = null;
        for (var attempt = 0; attempt < 20 && redemption?.Status != "Complete"; attempt++)
        {
            var redemptions = await client.GetFromJsonAsync<List<RedemptionResponse>>(
                $"/api/v1/sub-accounts/{clientCompanyId}/redemptions", cancellationToken);
            redemption = redemptions?.FirstOrDefault();
            if (redemption?.Status != "Complete")
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return redemption ?? throw new InvalidOperationException("redemption never completed.");
    }

    private static async Task<bool> PollForDeliveredNotificationAsync(
        TreasuryServiceOrchestratorDbContext dbContext, string eventType, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var delivered = dbContext.NotificationOutboxEntries.AsNoTracking()
                .Any(e => e.EventType == eventType && e.Status == NotificationDeliveryStatus.Delivered);
            if (delivered)
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        return false;
    }

    private sealed record SubAccountResponse(string ClientCompanyId, string ComplianceState);
    private sealed record BalanceResponse(decimal Amount, string CurrencyCode);
    private sealed record BalanceSnapshotResponse(decimal Amount, string CurrencyCode, string Reason, DateTime CreatedAtUtc);
    private sealed record TransactionResponse(Guid Id, string ClientCompanyId, string Type, string Status);
    private sealed record RecipientResponse(Guid Id, string Status);
    private sealed record LinkedBankAccountResponse(Guid Id, string Status);
    private sealed record RedemptionResponse(Guid Id, string Status, decimal GrossAmount, decimal? Fees, decimal? NetAmount);
    private sealed record MasterAccountSummaryResponse(BalanceResponse MainWalletBalance, BalanceResponse TotalSubAccountBalance, int SubAccountCount);
}
```

This test's DTOs (`SubAccountResponse`, `TransactionResponse`, etc.) intentionally mirror the response shapes each controller task already produces — if a controller's actual JSON property names drifted from this list while implementing Tasks 1-13, fix the drift in the controller/DTO, not in this test, since this test is transcribing the PRD's own acceptance sentence.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TreasuryServiceOrchestrator.IntegrationTests -- --filter-class "*DemoScriptEndToEndTests"`
Expected: FAIL initially if any prior task's endpoint/DTO shape doesn't match — that mismatch must be fixed in the source task's controller/DTO before this step is considered done, since this test's field names are taken directly from PRD §15.1 and the tasks that implement it, not invented fresh here.

- [ ] **Step 3: Run the full test suite to confirm the whole plan is green together**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all tests green, including `DemoScriptEndToEndTests`.

- [ ] **Step 4: Commit**

```bash
git add tests/TreasuryServiceOrchestrator.IntegrationTests/DemoScriptEndToEndTests.cs
git commit -m "test: PRD §15.1 demo-script end-to-end acceptance test"
```

---