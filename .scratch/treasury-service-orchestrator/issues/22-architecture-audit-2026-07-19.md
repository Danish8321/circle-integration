Status: open
Priority: P1 — findings from a full architectural audit (2026-07-19). Each finding below is
independently triageable; split into its own ticket when scheduled.

Type: audit

Source: full-repo architectural audit requested 2026-07-19, run against CLAUDE.md invariants
(1–12), the tier `must-not` rules (`.claude/rules/*.md`), and the testing strategy. Read-only —
no code changed. `test-fast.sh` remains 402/402 (ticket 21 fix in place).

## Verified clean

- **Project dependency rule**: Domain (no refs) ← Application → Domain ← Infrastructure →
  Application; Api → Infrastructure + Application. Arrows point inward only. No violation.
- **INV1** no `IRepository<T>`. **INV2** no `DateTime.Now/UtcNow` (only doc-comment mentions).
  **INV3** no `new HttpClient` (only a doc-comment mention). **INV10** no float/double money.
- **INV4** `CancellationToken` threaded on every handler (multi-line signatures; xUnit1051 is a
  build error).
- **INV6** single global `ValidationActionFilter` (`Program.cs:38`) — no controller hand-rolls
  validation.
- **INV9** mock mode gated by a hard env check (`MockModeGuard.Validate`, throws on Production),
  not config alone.
- **Idempotency/dedup indexes**: unique `(TenantId, IdempotencyKey)`; unique webhook
  `CircleEventId`; unique ledger `Transaction.ProviderReferenceId`; unique
  `(SubAccountId, Chain, Currency)` deposit address.
- Tenant-facing repo queries all carry a `ClientCompanyId` predicate; the tenant-less lookups
  (`GetByProviderReferenceIdAsync`, `FindByCircle*IdAsync`) are called only from system-context
  paths (reconciliation / webhook tenant-discovery) — no live cross-tenant read.

## Findings

> **Fixed 2026-07-19:** F1, F4, F5 (test-fast 402/402), then F6 (test-fast 404/404, ticket 23).
> **F8 INVALID** (misdiagnosis — column is NOT NULL; see F8). **F3 documented** (per-client
> breakers kept, see F3). **F2 fixed** (FakeStablecoinGateway for dev, see F2). **F7 fixed**
> (global query filter, ticket 24 — code in commit a1dd6de, Testcontainers proof pending Docker).
> All findings triaged; nothing open.

### F1 — Domain entity leaks past the Application boundary (INV5). Correctness/layering. RESOLVED.
Added `AdminTransactionResult` Application DTO; `ListAllTransactionsQueryHandler` now returns it;
`AdminTransactionResponse.Map` takes the DTO. Api tier no longer touches `Domain.Transaction`.
`ListAllTransactionsQueryHandler.HandleAsync` returns `IReadOnlyList<Domain.Transaction>`.
`AdminTransactionsController` (`using ...Domain`) maps the raw entity in-tier via
`AdminTransactionResponse.Map`. Every other list handler returns a `*Result` DTO. The Domain
entity crosses into the Api tier — the exact thing INV5 / `api.md` forbid.
**Fix**: add an `AdminTransactionResult` Application DTO; map inside the handler.

### F2 — Development (non-mock) wires a fake + a real gateway. RESOLVED (2026-07-19).
Added `FakeStablecoinGateway` (deterministic, no-network, all 8 `IStablecoinGateway` methods;
status literals map to usable states — recipient "active", linked bank "complete"). Development
now wires `ISubAccountGateway → FakeSubAccountGateway` and `IStablecoinGateway →
FakeStablecoinGateway` together; the real `CircleMintGateway` HTTP client is gone from the dev
branch (still wired in the Production branch). Both Circle gateways fake together or neither — no
more live mint/redeem/transfer calls against sub-accounts Circle never saw. "Full mock-mode" was
not chosen: the formal mock-provider system (PRD §13) doesn't exist in this repo yet. check.sh
clean, test-fast 404/404. No test for the fake (deterministic stub, mirrors `FakeSubAccountGateway`).

### F3 — Circuit breaker is per-typed-client, not shared. RESOLVED (documented, 2026-07-19).
Decision: **keep per-client breakers** (endpoint-level isolation), do not share a Circle-wide
breaker. `AddResilienceHandler` builds a separate pipeline (hence breaker state) per typed client
despite the shared `circle-resilience` name/config; a storm on one Circle endpoint must not
fast-fail calls to another. Trade-off accepted: a genuine provider-wide outage trips each breaker
separately rather than once. Recorded in `CircleResiliencePipelineFactory` remarks with the
revisit trigger (share a `CircuitBreakerStateProvider` if provider-wide outages become dominant).
No behavior change.

### F4 — Stale doc comment. Trivial. RESOLVED.
`CircleResiliencePipelineFactory` remarks corrected to say it is wired via
`AddCircleResilienceHandler`.

### F5 — Duplicated HttpClient config. Minor DRY. RESOLVED.
Extracted `static void ConfigureCircleClient(IServiceProvider, HttpClient)` in Program.cs; all 3
`AddHttpClient` registrations use the method group.

### F6 — Money-moving handlers commit in two separate transactions (INV11 atomicity). RESOLVED (ticket 23).
Fixed via Option C (persisted reservation). `IIdempotencyService` gained `TryBeginAsync`
(stages an `InProgress` record, SaveChanges #1 before the gateway) / `CompleteAsync` (flips to
`Completed`, SaveChanges #2). `IdempotencyExecutor` reserves before the provider call and commits
the completion together with the deferred ledger posting (`LedgerPostingService.PostAsync(...,
deferCommit: true)`) and aggregate in one atomic SaveChanges #2. The after-gateway crash case is
now self-healing via re-drive (`InFlightRetry`). Migration `AddIdempotencyReservationState`
(Status backfilled `Completed`, ResultJson→nullable). test-fast 404/404.
Original finding follows.


`CreateTransferCommandHandler` (and the `ProcessPayoutStatus` redemption debit) run:
gateway call → `LedgerPostingService.PostAsync` **SaveChanges #1** (ledger) → back in
`IdempotencyExecutor` **SaveChanges #2** (idempotency record + aggregate row). There is **no
wrapping transaction** anywhere in the repo (`grep` for `BeginTransaction`/`TransactionScope`/
execution strategy = empty); `UnitOfWork` is a thin `SaveChangesAsync`.
A crash between the two commits leaves the ledger posted but the idempotency record + `Transfer`
aggregate absent.
- Money double-spend is **prevented** by the unique `Transaction.ProviderReferenceId` index (a
  re-post with the same Circle id fails the insert).
- But the retry then dead-ends: gateway re-called (Circle dedups), `PostAsync` insert violates
  the unique index → 500 forever, and the `Transfer` row / idempotency record never persist.
  End state: ledger correct, aggregate missing, operation permanently un-completable.
**Fix**: store the idempotency record in the **same** `SaveChanges` as the ledger posting, or wrap
reserve→gateway→complete in one DB transaction. Reconsider "reserve = cache check": a cache check
is not a persisted reservation.

### F7 — Tenant isolation is by-convention, not structural (INV7). RESOLVED (ticket 24, 2026-07-19).
Fixed via a global EF `HasQueryFilter` on all 11 tenant-owned entities, sourced from the ambient
`ICallerContext`: `IsAdmin || ClientCompanyId == CallerId`. A regular caller is now restricted to
its own rows by construction; admin rides the `IsAdmin` bypass; an empty `CallerId` matches nothing
(fails closed). System/discovery/background reads opt out explicitly with `.IgnoreQueryFilters()`
(the 5 provider-id lookups + `GetByCircleWalletId` + the three cross-tenant background reads);
`ScheduledBalanceSnapshotService` now `Set`s the per-tenant caller before its scoped read. Query
filters are model metadata — no schema change. Code in commit a1dd6de (build clean, unit 404/404);
the EF filter itself is proven by `TenantIsolationQueryFilterTests` (Testcontainers, test-full).
INV7's CLAUDE.md wording ("structurally impossible at the data-access layer") already matches — no
text change. Original finding follows.

INV7 requires cross-tenant access be "structurally impossible at the data-access layer." Actual
mechanism: every repo query method takes a `clientCompanyId` parameter that callers must remember
to pass; there is no EF `HasQueryFilter` and no owned tenant-context type. `GetByProviderReference`
/`FindByCircle*Id` compile and run with no tenant predicate at all. No live breach today (those
are system-context only), but nothing structurally stops a future tenant-facing handler from
calling one and leaking. **Fix**: add a global `HasQueryFilter(e => e.ClientCompanyId == tenant)`
on tenant-owned entities (with an explicit system-context bypass for webhook/reconciliation), so
isolation holds by construction.

### F8 — Unfiltered unique index on a nullable column. INVALID (misdiagnosis, verified 2026-07-19).
Premise was wrong: `Transaction.ProviderReferenceId` is **not nullable**. Verified:
- EF config `entity.Property(x => x.ProviderReferenceId).IsRequired()` → column is
  `nvarchar(128) NOT NULL` (model snapshot line 657).
- Domain `Transaction.Create` throws `ArgumentException` on null/empty/whitespace
  (`Transaction.cs:46-49`), so no empty reference can be persisted.
- `LedgerPosting.ProviderReferenceId` is a non-nullable `string` record parameter.
A unique index on a NOT NULL column has no single-NULL limitation — no collision is possible. The
original finding assumed a nullable column and does not hold. **No fix, no migration.** If a future
internal/non-provider posting ever needs a null reference, the column nullability + domain guard
would change first, and the filtered-index question would be re-triaged then.

## Not exhaustively traced
Per-handler INV11 walk covered the money-moving set (transfer, redemption payout, deposit) and the
idempotency infra; the non-money mutating handlers (compliance decisions, status processors) were
sampled, not each line-traced. INV12 (no Travel-Rule request fields) relied on the existing
`circle_travel_rule_fix` memory + `CreateTransferCircleRequest`, not re-verified against live docs
this pass.
