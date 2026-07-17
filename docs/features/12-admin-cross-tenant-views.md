# Admin Cross-Tenant Views

Covers the three admin-only "see everything" reads PRD §2.5 lists that no other feature file
owns: a balance-summary column on the existing all-sub-accounts list, an all-tenants transaction
view, and a Master Account (Distributor) treasury-position summary. The one-sub-account
drill-down row in PRD §2.5's table needs no new code and is documented here only to close out the
table, not because it introduces anything.

Terminology (`ClientCompanyId`, Admin role, caller identity vs. target scope) is canonical in
`CONTEXT.md` "Tenancy & actors" and `01-tenancy-and-authorization.md` — this file does not restate
it. `Transaction`, `TransactionType`/`TransactionStatus`, `FundAccount`, `BalanceSnapshot`,
`ITransactionRepository`, `TransactionListFilter`, and the ledger-posting module are all owned by
`04-ledger-and-balances.md`; this file only documents how its own handlers/controllers consume
those types, never redefines them. Module ownership: `Admin` (`Application/Admin/`), a genuinely
separate module from `Ledger`/`Compliance` per `docs/adr/0001-module-boundaries.md` — not a
sub-namespace of either, since it composes ports from both without owning either's entities.

---

## 1. Scope / requirement

Source: PRD §2.5 (Admin portal views), PRD §15.1 slice 8 ("Admin cross-tenant views +
master-account summary, served from mock/ledger data"), `Phase_1_Feature_Slices.md` Task 12.

PRD §2.5's table lists five views; this file's scope for each:

| View | Endpoint shape | This file's scope |
|---|---|---|
| All sub-accounts | `GET /sub-accounts` | Extend the **existing** list endpoint (`01`/foundation, `Api/Compliance/SubAccountsController.cs`) with a current-balance-summary column. No new route. |
| One sub-account drill-down | `GET /sub-accounts/{clientCompanyId}` + tenant-scoped reads | No new code — documented in §2.2 only. |
| All transactions | `GET /transactions?clientCompanyId=...` (filter optional for Admin) | New `AdminTransactionsController`, consumes `04`'s `ITransactionRepository.ListAllAsync`. |
| Main (Distributor) account | `GET /master-account/balances`, `/deposits`, `/wire-instructions`, `/bank-accounts` | **Out of scope** except the summary row below — see §5. |
| Aggregate treasury position | `GET /master-account/summary` | New `MasterAccountController`, new `Admin/GetMasterAccountSummaryQuery`. |

Per PRD §2.4/§2.5 and `01-tenancy-and-authorization.md` §2.4's resolution table: Admin naming no
target on a list/aggregate endpoint resolves to `TenantScope.AllTenants`; the Master Account
summary is **not tenant-keyed at all** — it is not modeled by `TenantScope` (there is no
`ClientCompanyId` to resolve against), it is simply an Admin-only endpoint gated on
`ICallerContext.IsAdmin` directly, the same way `05`'s central exception handling gates
`TenantForbiddenException` mapping.

---

## 2. Domain & Application design

No new Domain entity. This is a read-composition feature: it queries entities and repositories
owned by `01` (`SubAccount`) and `04` (`Transaction`, `FundAccount`, `BalanceSnapshot`) and
composes their results into Admin-facing shapes.

### 2.1 Balance-summary column on the sub-account list

`Application/Compliance/GetSubAccount/SubAccountDetailsResult.cs` (owned by the Compliance
module, foundation-shipped) is extended with one additional field:

```csharp
public sealed record SubAccountDetailsResult(
    Guid SubAccountId,
    string ClientCompanyId,
    string LifecycleState,
    bool IsDisabled,
    string? CircleWalletId,
    string? LatestRegistrationStatus,
    string? RegistrationRejectionReason,
    Money? CurrentBalance = null);
```

`Money? CurrentBalance`, not `Money`, defaulted to `null` — a sub-account with no `FundAccount`
row yet (never received a deposit) has no balance to report, and `04`'s own
`GetCurrentBalanceQueryHandler` already establishes the convention that "no `FundAccount` exists
yet" is a distinct state from "balance is zero" (see `04` §4.1's `Money.Zero("USD")` open
question — this file's `CurrentBalance` sidesteps that ambiguity entirely by staying `null` rather
than picking a placeholder currency). The parameter defaults to `null` so `GetSubAccountHandler`'s
existing single-tenant call site (used by `GetSubAccount`, which does not need a balance column —
callers get their real balance from `04`'s `GET .../balances`) needs no change.

`SubAccountDetailsMapper.Map` (`Application/Compliance/SubAccountDetailsMapper.cs`) stays the
shared mapper for the fields it already owns; `ListSubAccountsHandler` populates `CurrentBalance`
itself after mapping, since only the list path has a reason to pay for an extra lookup per row:

```csharp
public sealed class ListSubAccountsHandler(
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations,
    IFundAccountRepository fundAccounts,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ICallerContext callerContext)
{
    public async Task<IReadOnlyList<SubAccountDetailsResult>> HandleAsync(
        ListSubAccountsQuery query, CancellationToken cancellationToken = default)
    {
        if (!callerContext.IsAdmin || query.Scope is not TenantScope.AllTenants)
        {
            throw new TenantForbiddenException();
        }

        await auditLog.AppendAsync(
            "SubAccountsListed", "SubAccount", "*",
            JsonSerializer.Serialize(new { LifecycleState = query.LifecycleState?.ToString() }),
            callerContext.CallerId, query.CorrelationId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var listed = await subAccounts.ListAsync(query.LifecycleState, cancellationToken);

        var results = new List<SubAccountDetailsResult>(listed.Count);
        foreach (var subAccount in listed)
        {
            var registration = await entityRegistrations.GetLatestForSubAccountAsync(
                subAccount.Id, cancellationToken);
            var fundAccount = await fundAccounts.GetByClientCompanyIdAsync(
                subAccount.ClientCompanyId, cancellationToken);
            var mapped = SubAccountDetailsMapper.Map(subAccount, registration);
            results.Add(mapped with { CurrentBalance = fundAccount?.Balance });
        }

        return results;
    }
}
```

Adding the `IFundAccountRepository fundAccounts` constructor parameter is the only shape change
to the handler; the existing Admin-gating check, the all-tenant audit call, and the
`SaveChangesAsync` ordering (audit persisted before the unfiltered list query runs, per
`01-tenancy-and-authorization.md` §2.6 mechanism 2) are unchanged — this feature only adds a
lookup after the row is already mapped, it does not touch the tenant-filtering/audit logic that
`01` already covers.

One `GetByClientCompanyIdAsync` call per row (N+1) is acceptable at Phase 1's demo/mock data
volume; batching into a single query is flagged for whoever scales the admin list past a page (see
§6).

### 2.2 One sub-account drill-down — no new code

`GET /sub-accounts/{clientCompanyId}` and every tenant-scoped read endpoint (`04`'s
`TransactionsController`, `BalancesController`) already resolve an Admin-named target via
`TenantScopeResolver.Resolve(caller, routeClientCompanyId)` (`01-tenancy-and-authorization.md`
§2.4): for an Admin caller naming a tenant in the route, `Resolve` returns
`TenantScope.SingleTenant(thatTenant)`, identical in shape to what a SubAccount caller gets for
its own id. The controller and handler code paths do not know or care whether the caller reaching
`SingleTenant` was an Admin naming another tenant or a SubAccount naming itself — the whole point
of `01`'s design is that this case needs no branching. Nothing in this file adds a route, handler,
or test for the drill-down; it is listed in PRD §2.5's table but has zero implementation surface
of its own.

### 2.3 All-tenants transaction view

`Application/Admin` does not own `Transaction` or `ITransactionRepository` — both are `04`'s. This
feature only adds the query/handler pair that calls `04`'s `ListAllAsync`:

```csharp
// Application/Ledger/ListAllTransactionsQuery.cs — owned by 04, shown here for reference only
public sealed record ListAllTransactionsQuery(TransactionListFilter Filter);

public sealed class ListAllTransactionsQueryHandler(ITransactionRepository transactions)
    : IQueryHandler<ListAllTransactionsQuery, IReadOnlyList<Transaction>>
{
    public async Task<IReadOnlyList<Transaction>> HandleAsync(
        ListAllTransactionsQuery query, CancellationToken cancellationToken = default) =>
        await transactions.ListAllAsync(query.Filter, cancellationToken);
}
```

(`04-ledger-and-balances.md` §4.2 documents this pair as living in the `Ledger` module rather
than `Admin`, on the grounds that it is a thin pass-through over a `Ledger`-owned port with no
Admin-specific composition logic — unlike `GetMasterAccountSummaryQuery` below, which does compose
across modules. This file does not relitigate that placement; it is `04`'s call since `04` owns
the port.)

`Api/Admin/AdminTransactionsController.cs` is the actual admin-specific surface — it builds the
`TransactionListFilter` from query parameters and enforces the Admin gate before calling the
handler:

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/transactions")]
public sealed class AdminTransactionsController(
    ICallerContext caller,
    IQueryHandler<ListAllTransactionsQuery, IReadOnlyList<Transaction>> listAllHandler)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? clientCompanyId,
        [FromQuery] TransactionType? type,
        [FromQuery] TransactionStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        if (!caller.IsAdmin)
        {
            throw new TenantForbiddenException("Only Admin may list transactions across tenants.");
        }

        var filter = new TransactionListFilter(
            clientCompanyId, type, status, fromUtc, toUtc,
            Page: page <= 0 ? 1 : page,
            PageSize: pageSize <= 0 ? 20 : pageSize);
        var result = await listAllHandler.HandleAsync(new ListAllTransactionsQuery(filter), cancellationToken);
        return Ok(result);
    }
}
```

The `caller.IsAdmin` check here is a **direct role gate**, not a `TenantScopeResolver.Resolve`
call — this endpoint has no per-tenant route segment for `Resolve` to arbitrate (`clientCompanyId`
is an optional filter, not a target scope), so the pattern is the same direct-check shape
`ListSubAccountsHandler` already uses as its own defense-in-depth (`01` §2.6 mechanism 2), applied
at the controller instead of the handler since there is no handler-level scope object to
double-check against here. `TransactionListFilter.ClientCompanyId` left `null` means "every
tenant" (`04` §4.2's `ListAllAsync` EF implementation applies each filter field as an optional
`Where`) — this is intentional per PRD §2.5 ("filter optional for admin"), not a gap: a SubAccount
caller never reaches this controller at all (rejected before the filter is even built), so there
is no risk of an unscoped query leaking to a non-admin caller.

### 2.4 Master Account summary

`Application/Admin/GetMasterAccountSummaryQuery.cs`:

```csharp
namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed record GetMasterAccountSummaryQuery;

public sealed record GetMasterAccountSummaryResult(
    Money MainWalletBalance, Money TotalSubAccountBalance, int SubAccountCount);
```

`Application/Admin/GetMasterAccountSummaryQueryHandler.cs`:

```csharp
public sealed class GetMasterAccountSummaryQueryHandler(
    ISubAccountRepository subAccounts,
    IBalanceSnapshotRepository snapshots,
    IStablecoinGateway gateway)
    : IQueryHandler<GetMasterAccountSummaryQuery, GetMasterAccountSummaryResult>
{
    public async Task<GetMasterAccountSummaryResult> HandleAsync(
        GetMasterAccountSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var mainWalletBalance = await gateway.GetMainWalletBalanceAsync(cancellationToken);
        var all = await subAccounts.ListAsync(lifecycleState: null, cancellationToken);

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

This is the one genuinely Admin-owned composition in this file: it reaches into `01`'s
`ISubAccountRepository`, `04`'s `IBalanceSnapshotRepository`, and a new `IStablecoinGateway`
method — none of those three ports belongs to `Admin`, but the composition itself (main wallet +
sum of latest snapshots) has no home in any single one of them, which is exactly why `Admin` is a
separate module rather than a sub-namespace of `Ledger` (per ADR 0001): this handler's reason to
exist is cross-module aggregation for the portal dashboard, not ownership of any entity.

`Total is summed from each sub-account's *latest* `BalanceSnapshot`, not `FundAccount.Balance`
directly — `04` §2.4 establishes `BalanceSnapshot` as the point-in-time balance record; using
`GetLatestAsync` here (rather than `IFundAccountRepository.GetByClientCompanyIdAsync`, used for
the list column in §2.1) keeps this handler's "as of" semantics explicit and matches PRD §2.5's
own wording ("sum of all sub-account balances from latest snapshots"). A sub-account with no
snapshot yet contributes `0m`, mirroring §2.1's `null`-then-summed-as-zero handling — the two call
sites use different port shapes for a reason (list column wants "no balance yet" to render as
blank; summary wants a number to add), not by oversight.

`IStablecoinGateway.GetMainWalletBalanceAsync(CancellationToken)` (new method on `04`'s stablecoin
gateway port — technically Ledger-owned, added here because this feature is the method's only
caller) maps to the live Circle call in §3. Its `CircleMintGateway` implementation is real (not a
placeholder) because §3 below verifies the exact wire shape; `MockStablecoinGateway` returns
`MockProviderOptions.MainWalletBalanceAmount` (new option, default `10_000m`) per `02-mock-mode.md`
convention — a fixed configurable value, not a randomized/simulated one, consistent with the rest
of mock mode's deterministic-by-default posture.

`Api/Admin/MasterAccountController.cs`:

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/master-account")]
public sealed class MasterAccountController(
    ICallerContext caller,
    IQueryHandler<GetMasterAccountSummaryQuery, GetMasterAccountSummaryResult> summaryHandler)
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

Same direct `caller.IsAdmin` gate as §2.3, for the same reason: there is no tenant-scoped route
segment on `/master-account/summary` for `TenantScopeResolver` to arbitrate — this surface is not
tenant-keyed at all (§1).

---

## 3. Circle provider mapping (verified live, 2026-07-17)

| Operation | Circle endpoint | Verified behavior |
|---|---|---|
| Master Account main-wallet balance (`IStablecoinGateway.GetMainWalletBalanceAsync`) | `GET /v1/businessAccount/balances` — **`walletId` omitted** | Live-verified via Circle API reference (`https://developers.circle.com/api-reference/circle-mint/account/list-business-balances`): `walletId` is an optional query parameter; when omitted, "the default is the main wallet of the account" — the response is the Distributor's primary-wallet balance only, not an aggregate across all entities/wallets. This confirms the hazard already on record in `Phase_1_Feature_Slices.md` line 326 (the `source`-omission hazard on transfers/payouts): omitting an entity-scoping parameter routes to the Distributor's own funds, never "everything." Every other feature's `walletId`-scoped call (`09-deposits.md`, sub-account balance reads) must pass `walletId` explicitly for exactly the inverse reason — this handler is the **one deliberate exception**, since the whole point of `GetMainWalletBalanceAsync` is to read the Distributor's own primary wallet. |
| Sub-account/tenant current balance (contrast, not this file's call) | `GET /v1/businessAccount/balances?walletId=…` | Same endpoint, `walletId` supplied — out of scope here, owned by `04`'s `GetCurrentBalanceQueryHandler`, shown only to make the omission-vs-explicit contrast concrete. |
| Balance history at the provider | *(none)* | No live Circle endpoint returns historical balance points for a wallet — confirmed via the Circle API reference index and search; the only balance-shaped endpoint is the current-balance one above, plus separate list endpoints for `deposits`/`transfers`/`payouts` activity (not balance snapshots). This is the stated rationale for `04`'s local `BalanceSnapshot` ledger (PRD §9.1: "the provider exposes only a current balance... there is no balance-history endpoint at the provider") and is consistent with everything read across this documentation pass — not independently re-derived here, just re-confirmed as still true. |

`CircleMintGateway.GetMainWalletBalanceAsync` therefore issues a real, unauthenticated-by-`walletId`
GET to `/v1/businessAccount/balances` and maps the response's balance entry to `Money` (currency
`USDC`, matching every other stablecoin-denominated amount in this codebase) — this is not a `0m`
placeholder implementation; the wire call itself is simple enough (single GET, no `walletId`, no
idempotency key since it is a read) that there is no reason to defer it the way a money-moving
gateway call might be.

---

## 4. Tests required

Per the testing strategy in `.claude/CLAUDE.md`.

**Domain** — n/a; no new Domain entity or invariant.

**Application** (xUnit v3, Moq, FluentAssertions — matching the shipped style in
`tests/TreasuryServiceOrchestrator.UnitTests/Compliance/ListSubAccountsHandlerTests.cs`, not the
NSubstitute snippets in `Phase_1_Feature_Slices.md`'s Task 12 source text; see §6):

- `ListSubAccountsHandlerTests` (existing file, extended) — new cases: `CurrentBalance` populated
  from `IFundAccountRepository.GetByClientCompanyIdAsync` when a `FundAccount` row exists; `null`
  when it does not. The four existing cases (Admin+AllTenants succeeds and audits; SingleTenant
  scope throws; non-admin caller throws even with `AllTenants` scope; lifecycle filter passed
  through) are unchanged and must keep passing — this feature only adds a field, it does not touch
  the tenant-gating logic under test there.
- `ListAllTransactionsQueryHandlerTests` — owned by `04-ledger-and-balances.md` §5 (delegates to
  `ITransactionRepository.ListAllAsync` with the constructed filter, no tenant check at this
  layer). Not duplicated here.
- `GetMasterAccountSummaryQueryHandlerTests` (new) — Moq-based, mirroring the shipped style:
  - Sums each sub-account's latest `BalanceSnapshot.Balance.Amount` plus the gateway's main-wallet
    balance; asserts `SubAccountCount` matches the sub-account list length.
  - A sub-account with no snapshot yet (`GetLatestAsync` returns `null`) contributes `0m`, not a
    thrown exception or a skipped row.
  - **No caller-role assertion in this handler's own tests** — `GetMasterAccountSummaryQuery` has
    no `TenantScope`/`ICallerContext` dependency of its own (unlike `ListSubAccountsHandler`); the
    Admin gate lives entirely in `MasterAccountController` (§2.4), so the structural-rejection
    assertion belongs at the Api layer, not here. This mirrors why `ListAllTransactionsQueryHandler`
    (§2.3) also carries no tenant check of its own.

**Api** (WebApplicationFactory + Testcontainers, real SQL Server — per the Api-tier rule that
in-memory EF fakes are forbidden):

- `AdminTransactionsControllerTests` — Admin caller with no `clientCompanyId` filter gets every
  tenant's transactions; Admin caller with `clientCompanyId` set gets one tenant's; **a SubAccount
  caller gets `403 tenant-forbidden` structurally** — asserted by hitting the endpoint with a
  SubAccount `ClientCompanyId` header and checking the response status/RFC 7807 body, not by
  inspecting whether a conditional branch was taken. This is the explicit "assert non-admin
  callers rejected structurally" requirement from the `.scratch` ticket's definition of done.
- `MasterAccountControllerTests` — Admin caller gets `200` with `MainWalletBalance`,
  `TotalSubAccountBalance`, `SubAccountCount`; **SubAccount caller gets `403 tenant-forbidden`**,
  same structural assertion.
- `ListSubAccountsControllerTests` (extended, or a new case in the existing
  `SubAccountsControllerTests.cs`) — Admin's `GET /sub-accounts` response includes a
  `currentBalance` field per row (`null` for a sub-account with no deposits yet, populated once a
  `FundAccount` exists) — full-stack proof the DTO shape change round-trips through the real
  pipeline, not just the handler unit test.
- Drill-down (§2.2) — **no new test** required by this file; already covered end-to-end by
  `01-tenancy-and-authorization.md` §3's `GetSubAccountTests.cs`/`ResubmitEntityRegistrationTests.cs`
  (Admin naming another tenant's `ClientCompanyId` in the route succeeds; SubAccount naming
  another tenant's gets `403`) — this file adds nothing to that surface, so it adds nothing to
  that test file either.

"Assert non-admin callers rejected structurally" — for all three new/changed endpoints in this
file, "structurally" means: the rejection happens via `caller.IsAdmin` (a property read directly
off `ICallerContext`, populated once per request by `CallerIdentityMiddleware` before any
controller code runs — `01` §2.2) or `TenantScopeResolver`'s own Admin branch, never a
`clientCompanyId == caller.CallerId` string comparison that a crafted request could route around.
The Api-tier tests above prove this by hitting the real endpoint with a real (non-admin)
`ClientCompanyId` header through the full pipeline, not by mocking `ICallerContext.IsAdmin` to
return `false` and checking a unit-level branch — the latter would only prove the `if` statement
exists, not that nothing upstream could bypass it.

---

## 5. Open corrections / decisions log

**`/master-account/deposits`, `/master-account/wire-instructions`, `/master-account/bank-accounts`
— confirmed out of scope, not a gap.** PRD §2.5's table lists all four `/master-account/*`
sub-routes under one row, but PRD §15.1 slice 8's own title is "master-account **summary**," and
`Phase_1_Feature_Slices.md` Task 12's own scope note (line 8980) states plainly that none of the
three has a backing entity anywhere in the plan: no Distributor-level deposit ledger, no
`WireInstruction` entity, and `/master-account/bank-accounts` already has a real equivalent in
whichever feature file owns linked bank accounts (`08-banking-and-wire-instructions.md`, not yet
written as of this pass — `GET /api/v1/linked-bank-accounts`, Admin-only, Distributor-level, per
that feature's redemption/banking scope). Building the other three now would mean inventing
unmodeled entities with no consumer; this file follows the `.scratch/treasury-service-orchestrator/
issues/08-admin-cross-tenant-views.md` ticket's own scope line verbatim on this point — resolved,
not flagged, since both the PRD's own §15.1 title and the ticket agree.

**Test framework: Moq, not NSubstitute — `Phase_1_Feature_Slices.md` Task 12's code snippets
corrected.** The source doc's Step 1/Step 8 test snippets (lines 9006-9063, 9232-9311) use
`NSubstitute` (`Substitute.For<T>()`) throughout. The actual shipped test suite
(`tests/TreasuryServiceOrchestrator.UnitTests/Compliance/ListSubAccountsHandlerTests.cs`, read
directly for this file) uses `Moq` (`Mock<T>`), matching `.claude/CLAUDE.md`'s testing-strategy
table ("Application: xUnit v3, **Moq** (mock ports), FluentAssertions"). §4 above specifies Moq for
the new/extended tests in this feature — this is the same category of drift `01`'s and `04`'s
files already documented for other Phase 1 snippets (stale source-doc code blocks, not a live
decision to relitigate), extended here to the test-framework choice specifically.

**Shipped `ListSubAccountsHandler`/`SubAccountDetailsResult` shape already diverges from Task 12's
snippet baseline — this file layers onto the shipped shape, not the Task 12 snippet.** Task 12's
Step 3 snippet (lines 9072-9122) rewrites `SubAccountDetailsResult` and `ListSubAccountsHandler`
from scratch, assuming a simpler pre-existing shape (`SubAccountLifecycleState LifecycleState`
enum, `EntityRegistrationStatus? LatestRegistrationStatus`, a two-dependency handler
constructor with no audit logging). The shipped code
(`Application/Compliance/GetSubAccount/SubAccountDetailsResult.cs`,
`Application/Compliance/ListSubAccounts/ListSubAccountsHandler.cs`, both read directly for this
file) already has a richer shape than that snippet assumes: `LifecycleState`/
`LatestRegistrationStatus` are already `string` (via `.ToString()` in
`SubAccountDetailsMapper.Map`), and the handler already carries `IEntityRegistrationRepository`,
`IAuditLogService`, `IUnitOfWork`, `ICallerContext` and performs the all-tenant audit write before
listing (`01-tenancy-and-authorization.md` §2.6 mechanism 2) — none of which Task 12's snippet
baseline had, because Task 12's snippet was written against an earlier, pre-audit version of the
handler. §2.1 above documents the **actual** extension (add `IFundAccountRepository`, add
`CurrentBalance` to the record, populate it via `with` after `SubAccountDetailsMapper.Map`) against
the shipped baseline, not Task 12's stale one. This is a "shipped code moved on since the plan was
written" correction, the same category already flagged in `.claude/CLAUDE.md`'s own header note
("corrected 2026-07-17 to match code as shipped").

**`IStablecoinGateway.GetMainWalletBalanceAsync` — Task 12's snippet defers the real Circle call to
Phase 3 with a `0m` placeholder; this file implements it now.** `Phase_1_Feature_Slices.md` Step 7
(line 9210-9217) has `CircleMintGateway.GetMainWalletBalanceAsync` return
`Task.FromResult(new Money(0m, "USDC"))` unconditionally, with a comment deferring the real
`GET /v1/businessAccount/balances` (no `walletId`) call to "Phase 3." Since this documentation
pass live-verified that exact endpoint/parameter behavior (§3 above) and found nothing blocking
(no auth/entitlement gap different from any other Circle Mint call already in scope, no
rate-limit or availability caveat beyond what every other gateway call already handles), this file
specifies the real implementation rather than carrying the placeholder forward — a `0m` stub would
make the Master Account summary a known-wrong number in the demo (PRD §15.1's own acceptance
script), which is worse than the small cost of wiring the one extra GET call now. Flagged for
product/implementation confirmation since it is a scope increase versus the Task 12 snippet's
literal text, not silently changed.

**No discrepancy found in PRD §2.5's own table vs. `01`'s resolution-table wording for
`AllTenants` on list/aggregate endpoints, or vs. `04`'s `TransactionListFilter` shape.** Both
match what shipped/is documented elsewhere; no correction needed on those two points.
