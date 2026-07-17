# Tenancy & Authorization

Covers the caller registry, Admin vs SubAccount roles, caller-identity vs target-scope
resolution, the `TenantScope` closed hierarchy, and tenant filtering at the data-access layer.

Terminology (`ClientCompanyId`, `SubAccount`, caller identity vs target scope, Admin, SubAccount
role) is canonical in `CONTEXT.md` "Tenancy & actors" — this file does not restate definitions,
only design and behavior. Module ownership: `Application.Shared` (`ICallerContext`,
`TenantScopeResolver`, `TenantScope`) per `docs/adr/0001-module-boundaries.md`.

RFC 7807 `ProblemDetails` error contract (including the `tenant-forbidden` mapping) lives in
`05-reliability-and-error-handling.md` — out of scope here except to note that
`TenantForbiddenException` is thrown from this module and mapped there.

---

## 1. Scope / requirement

Source: PRD §2 (Actors & Authorization).

- Every request carries a `ClientCompanyId` HTTP header credential, validated against a registry
  of known callers.
- `ClientCompanyId` is the tenant identifier. Every persisted record (sub-account, entity
  registration, wallet, deposit address, transaction, balance snapshot, webhook-event effect,
  audit record) carries it as tenant key. All queries/mutations are scoped by it so cross-tenant
  access is structurally impossible, not just checked at the controller.
- Two roles in v1, no finer-grained user-level RBAC — the calling application is the security
  principal:
  - **Admin** — the APISO Portal's privileged credential. Maps to *no* sub-account. All
    operations on all sub-accounts, sub-account creation, linked bank account management,
    webhook replay.
  - **SubAccount** — each client company's own `ClientCompanyId` credential. All read and
    money-moving operations on its own sub-account only.
- Caller identity (who is asking) and target scope (whose data the request is about) are kept
  structurally distinct. Admin never impersonates a tenant: it always authenticates as itself and
  names the target scope explicitly (route/query), never by swapping its header value.
  All-tenant access is itself audited.
- The human portal user driving an admin action is passed in a separate header, recorded for
  audit only — it grants no permissions (not modeled by `CallerIdentityMiddleware` yet; tracked
  as an open item below).

---

## 2. Domain & Application design

### 2.1 Caller resolution — `CallerIdentityMiddleware`

`src/TreasuryServiceOrchestrator.Api/Middleware/CallerIdentityMiddleware.cs`

This is the one and only place a raw `ClientCompanyId` header value is read. Everything
downstream — controllers, handlers — consumes `ICallerContext`, never the header.

```csharp
public sealed class CallerIdentityMiddleware(RequestDelegate next)
{
    private const string HeaderName = "ClientCompanyId";

    public async Task InvokeAsync(
        HttpContext context,
        HttpCallerContext callerContext,
        IOptions<CallerIdentityOptions> options,
        ISubAccountRepository subAccountRepository)
    { ... }
}
```

Resolution logic, in order:

1. Header missing/blank → `401`.
2. Header value equals `CallerIdentityOptions.AdminCallerId` (config, `CallerIdentity:AdminCallerId`)
   → sets `CallerRole.Admin`, **no repository lookup** — the admin identity is a single
   configured value, not a persisted row.
3. Otherwise, looks up the value via `ISubAccountRepository.GetByClientCompanyIdAsync` — the
   sub-account registry *is* the `SubAccount` table itself (`ClientCompanyId` is unique, 1:1 with
   a `SubAccount` row per `CONTEXT.md`), not a separate static list. No match → `401`. Match →
   sets `CallerRole.SubAccount`.

There is no separate "known callers" list/registry type — this is a deliberate simplification
from the doc history (see §4 below).

`CallerIdentityOptions` (`Api/Middleware/CallerIdentityOptions.cs`):

```csharp
public sealed class CallerIdentityOptions
{
    public const string SectionName = "CallerIdentity";
    public string AdminCallerId { get; set; } = string.Empty;
}
```

### 2.2 `ICallerContext` port

`src/TreasuryServiceOrchestrator.Application/Shared/Ports/ICallerContext.cs`

```csharp
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

Implemented in the Api tier by `HttpCallerContext` (`Api/Middleware/HttpCallerContext.cs`) — a
mutable, per-request-scoped class the middleware calls `.Set(callerId, role)` on once resolution
succeeds; handlers only ever see it through the `ICallerContext` interface (registered in
`Program.cs` as `AddScoped<ICallerContext>(sp => sp.GetRequiredService<HttpCallerContext>())`).
This is the mechanism behind invariant 7 (tenant identity always from `ICallerContext`, never a
route/body parameter) — `ICallerContext` is populated exactly once per request, by the
middleware, before any controller action runs.

### 2.3 `TenantScope` — closed hierarchy

`src/TreasuryServiceOrchestrator.Application/Shared/TenantScope.cs`

```csharp
public abstract record TenantScope
{
    // "SingleTenant" rather than "Single": CA1720 forbids identifiers matching
    // primitive type names (System.Single).
    public sealed record SingleTenant(string ClientCompanyId) : TenantScope;

    public sealed record AllTenants : TenantScope; // Admin list only
}
```

Never a nullable string, never `Resolve(...)!` null-forgiving — every caller of
`TenantScopeResolver.Resolve` gets one of exactly two concrete cases and must pattern-match or
cast, not null-check.

### 2.4 `TenantScopeResolver` — caller identity vs. target scope

`src/TreasuryServiceOrchestrator.Application/Shared/TenantScopeResolver.cs`

```csharp
public static class TenantScopeResolver
{
    public static TenantScope Resolve(ICallerContext caller, string? requestedClientCompanyId)
    {
        if (caller.IsAdmin)
        {
            return requestedClientCompanyId is null
                ? new TenantScope.AllTenants()
                : new TenantScope.SingleTenant(requestedClientCompanyId);
        }

        if (requestedClientCompanyId is not null
            && !string.Equals(requestedClientCompanyId, caller.CallerId, StringComparison.Ordinal))
        {
            throw new TenantForbiddenException();
        }

        return new TenantScope.SingleTenant(caller.CallerId);
    }
}
```

Resolution table (PRD §2.4):

| Caller | Target named in route/query | Target omitted |
|---|---|---|
| **SubAccount** | Must equal caller's own id, else `TenantForbiddenException` → `403 tenant-forbidden`. | Implicitly the caller's own sub-account (`SingleTenant`). |
| **Admin** | That tenant (`SingleTenant`). | `AllTenants` (list/aggregate endpoints); Master Account is a separate, non-tenant-keyed admin-only surface — not modeled by `TenantScope` at all (out of scope for this file). |

`TenantForbiddenException()` (`Application/Exceptions/TenantForbiddenException.cs`, no
message argument — hardcodes `"Caller may not act on the requested tenant."`) derives from
`DomainException`; mapping to RFC 7807 is owned by `05-reliability-and-error-handling.md`.

### 2.5 Controller usage pattern

`src/TreasuryServiceOrchestrator.Api/Compliance/SubAccountsController.cs` is the reference
implementation of the pattern every controller follows:

- Route-scoped endpoints (`GetSubAccount`, `ResubmitEntityRegistration`,
  `SetSubAccountDisabled`, `CreateSubAccount`) call
  `TenantScopeResolver.Resolve(callerContext, routeOrBodyClientCompanyId)` and immediately cast
  the result to `TenantScope.SingleTenant` — safe because the route segment/body field is
  guaranteed non-empty by routing/validation, so the only other possible outcome is
  `TenantForbiddenException` (thrown inside `Resolve`, propagates to the central handler).
- List/aggregate endpoints (`ListSubAccounts`) call `Resolve(callerContext, null)` and pass the
  resulting `TenantScope` straight through to the handler/query object — for an Admin caller this
  is `AllTenants`; for a SubAccount caller it's `SingleTenant(callerId)`, which the handler must
  itself reject (see §2.6) since listing is Admin-only regardless of scope shape.
- **The controller never reads the `ClientCompanyId` header or route value as the tenant
  directly** — it only ever uses the value returned by `Resolve`, and only after resolution
  succeeded (satisfies Api tier rule: "must not read `ClientCompanyId`/tenant scope from anywhere
  but the validated caller credential/middleware").

### 2.6 Tenant filtering at the data-access layer

Two distinct mechanisms are in play, both structural (not controller-level checks):

1. **Key-scoped queries for `SingleTenant`.** Handlers that resolve to `SingleTenant` (e.g.
   `GetSubAccountHandler`) call `ISubAccountRepository.GetByClientCompanyIdAsync(scope.ClientCompanyId, ct)`
   — the repository query itself is parameterized by the resolved tenant id, so there is no code
   path in that handler that can return another tenant's row. This is the mechanism for
   single-tenant endpoints; there is no EF global query filter involved (no ambient/ThreadStatic
   tenant filter — the tenant id is an explicit query parameter every time).
2. **Role + scope double-check for `AllTenants`.** `ListSubAccountsHandler`
   (`Application/Compliance/ListSubAccounts/ListSubAccountsHandler.cs`) is the only handler today
   that can see `AllTenants`. It defends in depth rather than trusting the resolved scope alone:

   ```csharp
   if (!callerContext.IsAdmin || query.Scope is not TenantScope.AllTenants)
   {
       throw new TenantForbiddenException();
   }
   ```

   i.e. it re-checks `callerContext.IsAdmin` directly, not just the scope shape — so a future bug
   in `TenantScopeResolver` that accidentally produced `AllTenants` for a non-admin caller would
   still be caught at the handler. All-tenant access is then itself audited
   (`IAuditLogService.AppendAsync("SubAccountsListed", ...)`) before the unfiltered
   `ISubAccountRepository.ListAsync` query runs — satisfying PRD §2.4's "that access is itself
   audited."

`ISubAccountRepository` (`Application/Compliance/Ports/ISubAccountRepository.cs`) has no
tenant-id parameter on `ListAsync` at all — because the only caller of the unfiltered list path
is already gated to Admin/`AllTenants` as above; the port is use-case-shaped, not a generic
`IRepository<T>` with an ambient tenant filter (Infrastructure tier rule).

**Every future repository/port that persists or queries tenant-keyed data must follow mechanism
1** — accept the resolved `ClientCompanyId` as an explicit parameter, never an implicit/ambient
value — unless it is an Admin/`AllTenants` list-style port, in which case it must follow
mechanism 2 (handler-level role re-check + audit, mirroring `ListSubAccountsHandler`).

---

## 3. Tests required

Per the testing strategy in `.claude/CLAUDE.md`:

**Domain** — n/a; no domain entity/invariant lives in this slice (`CallerRole`, `TenantScope` are
plain Application-tier types, not Domain entities).

**Application** (xUnit v3, Moq, FluentAssertions) — `TenantScopeResolver` is a pure static
function; already exercised (see `tests/TreasuryServiceOrchestrator.UnitTests/Application/` —
verify a `TenantScopeResolverTests.cs` exists covering all four resolution-table cells):

- SubAccount caller, no requested scope → `SingleTenant(own id)`.
- SubAccount caller, requesting own id → `SingleTenant(own id)`.
- SubAccount caller, requesting another id → throws `TenantForbiddenException`.
- Admin caller, requesting a named tenant → `SingleTenant(that tenant)`.
- Admin caller, no requested scope → `AllTenants`.

`ListSubAccountsHandler`'s defense-in-depth branch needs its own handler-level unit test: Admin
caller + `AllTenants` scope succeeds and audits; any other combination throws
`TenantForbiddenException` even if a (hypothetical, buggy) resolver handed it `AllTenants` for a
non-admin caller — i.e. test the handler's own `IsAdmin` re-check independent of the resolver.

**Api** (WebApplicationFactory + Testcontainers) —
`tests/TreasuryServiceOrchestrator.UnitTests/Api/CallerIdentityMiddlewareTests.cs` (present,
unit-level with a mocked `ISubAccountRepository`) covers:

- Missing header → `401`, `next` not called, no repository lookup.
- Admin caller id → `CallerRole.Admin`, `next` called, **no repository lookup** (admin short-circuits
  before hitting the sub-account registry).
- Known sub-account caller id → `CallerRole.SubAccount`, `next` called.
- Unknown caller id → `401`, `next` not called.

Full-pipeline integration coverage (real Testcontainers SQL Server, real
`TreasuryServiceOrchestratorDbContext`-backed `ISubAccountRepository`) belongs in
`tests/TreasuryServiceOrchestrator.IntegrationTests/Compliance/` — cross-tenant rejection is
already exercised end-to-end via the sub-account controller tests
(`SubAccountsControllerTests.cs`, `GetSubAccountTests.cs`,
`ResubmitEntityRegistrationTests.cs`): a SubAccount caller naming another tenant's
`ClientCompanyId` in the route must get `403 tenant-forbidden` through the full stack, not just
at the resolver.

---

## 4. Open corrections / decisions log

**Caller registry: Phase_1 Task 1's plan vs. shipped code diverge; shipped code kept.**
`docs/Phase_1_Feature_Slices.md` Task 1 specifies a `KnownClientCompaniesRegistry` /
`KnownClientCompaniesOptions : List<KnownCaller>` backed by a flat configured list of
`{ Id, Role }` pairs, a `ClientCompanyIdMiddleware`, and `TryResolve(string, out KnownCaller)`.
None of this exists in the shipped codebase. What actually shipped
(`src/TreasuryServiceOrchestrator.Api/Middleware/CallerIdentityMiddleware.cs`,
`CallerIdentityOptions.cs`) instead treats the `SubAccount` table itself as the sub-account
registry (`ISubAccountRepository.GetByClientCompanyIdAsync`), with only the single Admin id
coming from config (`CallerIdentityOptions.AdminCallerId`). **Resolved: this file documents the
shipped code, not the Phase_1 plan** — the plan is a stale historical design that was superseded
during implementation (config-driven caller list would have required keeping a second list in
sync with the real `SubAccount` table, which the shipped design avoids structurally). The `.claude/CLAUDE.md`
project file itself already flags `ClientCompanyIdMiddleware` as a "stale name that appeared in
old docs, already corrected elsewhere this session" — this is the same drift, extended to the
whole registry design, not just the middleware's name.

**PRD §2.2 vs. shipped code: "registry of known callers" is ambiguous, both readings reconciled.**
PRD §2.2 says the header is "validated against a registry of known callers" without specifying
whether that registry is a separate configured list or the tenant table itself. Phase_1 Task 1
assumed a separate configured list; shipped code uses the tenant table plus one configured admin
id. **Resolved: shipped code's reading is correct and is what this file describes** — it is the
simpler design (no second source of truth to keep in sync with `SubAccount` creation/deletion)
and is what is actually running.

**Portal human-user audit header (PRD §2.2) — resolved 2026-07-17 grilling (ticket 14): explicitly
deferred.** PRD §2.2 states the identity of the human user driving a portal action is "passed in a
request header and recorded for audit only." `CallerIdentityMiddleware` has no such header
handling today — it resolves only `ClientCompanyId`. No portal/client exists in this repo
(API-only, no Angular/TS client per CLAUDE.md) and no portal authentication mechanism exists to
source a human identity from, so designing the header now would be speculative. Deferred until a
portal auth client is actually built — see `06-audit-and-compliance.md` §7 for the matching entry.

**RFC 7807 content intentionally excluded.** Phase_1 Task 2 bundles `TenantScopeResolver` and
the global `IExceptionHandler`/`ProblemDetails` mapping into one task. This file took only the
tenant-scope-resolution half per the assignment; the exception-hierarchy/`ProblemDetails`-mapping
half (including how `TenantForbiddenException` becomes a `403 tenant-forbidden` body) belongs to
`05-reliability-and-error-handling.md`.
