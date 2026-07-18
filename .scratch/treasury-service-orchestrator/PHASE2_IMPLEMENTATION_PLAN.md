# Phase 2 Implementation Plan

Status: draft, awaiting approval. Phase 1 (tickets 02-14) fully shipped and closed — see
`PHASE1_IMPLEMENTATION_PLAN.md`. Goal per `docs/README.md` line ~101: "Hardening: reconciliation
job live (before real money moves), webhook + notification dead-letter/replay, provider
resilience (Polly), scheduled balance snapshots, observability completion, full list-endpoint
pagination/filtering."

Order (per 2026-07-18 grilling — README's own listing order, reconciliation first since it's the
"before real money moves" gate): **15 (reconciliation) -> 16 (dead-letter/replay) -> 17 (Polly)
-> 18 (scheduled snapshots) -> 19 (observability) -> 20 (pagination)**.

Same verification contract as Phase 1: no task is "done" without its stated verification passing.
Migrations always hand-reviewed and shown to the user before `schema.sh apply`.

---

## Ticket 15 — Reconciliation job (`docs/features/05-reliability-and-error-handling.md` §7)

Fully spec'd already — no further grilling needed, design is settled. Blocked by: none (Phase 1
complete).

**Status: done, closed 2026-07-18.** All sub-tasks 15.1-15.7 shipped and verified: `check.sh`
clean full solution, `test-full.sh` 59/59 integration tests green (incl. 2 new reconciliation
tests), `test-fast.sh` 379/379 unit tests green.

### 15.1 — `ProviderDepositRecord` + `IStablecoinGateway.ListRecentDepositsAsync`
- Files: `Application/Ledger/Ports/{GatewayDtos.cs (add ProviderDepositRecord),
  IStablecoinGateway.cs (add ListRecentDepositsAsync)}`.
- Verify: `check.sh Application`.

### 15.2 — Mock-mode deposit ledger
- Files: `Infrastructure/Mocks/{IMockProviderDepositLedger,MockProviderDepositLedger}.cs`
  (Infrastructure namespace per §7.7 — mock-only seam, no production adapter),
  `Infrastructure/Mocks/MockStablecoinGateway.cs` (extend, delegates unchanged).
- Verify: `MockProviderDepositLedgerTests`, `MockStablecoinGatewayTests` (extended) ->
  `test-fast.sh Infrastructure`.

### 15.3 — Real gateway (both Circle calls, merged and tagged)
- Files: `Infrastructure/Providers/Circle/CircleMintGateway.cs` (extend — `GET
  /v1/businessAccount/deposits` for `Wire`, `GET /v1/businessAccount/transfers?destinationWalletId=`
  for `OnChain`, per §7.2).
- Verify: `test-fast.sh Infrastructure`.

### 15.4 — `ISubAccountRepository.ListActiveWithWalletAsync`
- Files: `Application/Compliance/Ports/ISubAccountRepository.cs` (add method),
  `Infrastructure/Persistence/SubAccountRepository.cs` (implement).
- Verify: `SubAccountRepositoryTests` (excludes inactive/disabled/walletless) -> `test-full.sh`.

### 15.5 — `DepositReconciliationService`
- Files: `Application/Ledger/Reconciliation/{DepositReconciliationService,
  ReconciliationOptions}.cs`.
- Change: exact pass logic per §7.4 — per-wallet and per-deposit try/catch, dedup via
  `ITransactionRepository.GetByProviderReferenceIdAsync`, reuses `ProcessDepositCommand` handler,
  correlation id `reconciliation-{providerReferenceId}`.
- Verify: `DepositReconciliationServiceTests` (all 5 cases in §8's table) -> `test-fast.sh
  Application`.

### 15.6 — Background service + DI + config
- Files: `Infrastructure/Reconciliation/DepositReconciliationBackgroundService.cs`, `Program.cs`,
  `appsettings.json` (`"Reconciliation"` section).
- Verify: `check.sh` full solution.

### 15.7 — Integration test
- Files: `tests/.../DepositReconciliationIntegrationTests.cs`.
- Change: seed phantom deposit via `IMockProviderDepositLedger.SeedAsync`, run `RunOnceAsync`
  directly (not the timer), assert real `Transaction`+`FundAccount` move; second pass self-heals
  zero.
- Verify: `test-full.sh`; `check.sh` full solution.

---

## Ticket 16 — Webhook + notification dead-letter/replay

`WebhookDeadLetterPolicy` (threshold 5) already exists — detection only, no replay action.
`NotificationOutboxEntry` has no equivalent policy at all. Blocked by: none.

### 16.1 — Notification outbox dead-letter policy
- Files: `Application/Webhooks/Ports/NotificationOutboxDeadLetterPolicy.cs` (mirrors
  `WebhookDeadLetterPolicy`'s shape — `AttemptThreshold = 5`, same constant, not re-derived
  differently for no reason).
- Verify: unit test mirroring `WebhookDeadLetterPolicy`'s own (none currently exists as a
  dedicated test file — add one for both if missing) -> `test-fast.sh Application`.

### 16.2 — Admin replay endpoints
- Files: `Application/Admin/{ReplayWebhookInboxEntryCommand,ReplayWebhookInboxEntryHandler,
  ReplayNotificationOutboxEntryCommand,ReplayNotificationOutboxEntryHandler}.cs`,
  `Api/Admin/DeadLetterController.cs`, `Program.cs`.
- Change: replay resets `Attempts = 0` (or a dedicated `Status = Pending`/re-queue, whichever the
  existing entities' state shape supports without a new column) and re-enqueues for the existing
  dispatcher/processor pipeline to pick up — replay is "try again through the normal path," not a
  bespoke second delivery mechanism. Admin-only (direct `caller.IsAdmin` gate, matches ticket 08.3
  precedent). Audit-logged (`"WebhookReplayed"` / `"NotificationReplayed"` events).
- Verify: `ReplayWebhookInboxEntryHandlerTests`, `ReplayNotificationOutboxEntryHandlerTests` ->
  `test-fast.sh Application`; `DeadLetterControllerTests` (Admin 200, SubAccount structural 403,
  replay actually reaches the underlying pipeline) -> `test-full.sh`; `contract.sh`; `check.sh`
  full solution.

---

## Ticket 17 — Provider resilience (Polly)

Real Circle HTTP gateways already exist and are wired via `AddHttpClient` (`Program.cs`) — not
blocked on Phase 3, buildable now. Design fully spec'd in
`docs/features/05-reliability-and-error-handling.md` §4. Blocked by: none.

### 17.1 — `CircleClientOptions` + Polly pipeline
- Files: `Infrastructure/Providers/Circle/CircleClientOptions.cs`,
  `Infrastructure/Providers/Circle/CircleResiliencePipelineFactory.cs` (or equivalent
  `AddResilienceHandler` extension on the named `"Circle"` client).
- Change: timeout per `TimeoutSeconds`; retry with exponential backoff on 5xx/429/timeout/
  `HttpRequestException` only, never other 4xx (§4); circuit breaker,
  `CircuitBreakerFailureThreshold` consecutive failures opens for `CircuitBreakerDurationOfBreak`,
  open circuit throws `ProviderUnavailableException` fast.
- Verify: `check.sh Infrastructure`; unit test `CircleClientOptionsTests` (binds from config,
  defaults match §4) -> `test-fast.sh Infrastructure`.

### 17.2 — Wire into `Program.cs`'s three `AddHttpClient<..., CircleMintGateway/
CircleSubAccountGateway>` registrations
- Files: `Api/Program.cs`, `appsettings.json` (`"Circle"` client options section).
- Verify: integration test asserting the named client resolves with the pipeline attached
  (existing `MockProviderWiringTests`-style pattern) -> `test-full.sh`.

### 17.3 — Retry/circuit-breaker behavior test
- Files: `tests/.../CircleResiliencePipelineTests.cs` (fixture `HttpMessageHandler` per §8's
  table note).
- Change: 4xx-other-than-429 asserts attempt count == 1 (no retry); 5xx/429/timeout retries up to
  `RetryCount` with backoff; N consecutive failures opens the circuit, next call fails fast without
  hitting the handler.
- Verify: `test-fast.sh Infrastructure`; `check.sh` full solution.

---

## Ticket 18 — Scheduled balance snapshots

`BalanceSnapshotReason.Scheduled` already exists in the Domain enum (docs/features/04, ticket
04.1) — this ticket is the periodic job that actually writes one. Blocked by: none.

### 18.1 — Snapshot-all service
- Files: `Application/Ledger/Snapshots/ScheduledBalanceSnapshotService.cs`.
- Change: `RunOnceAsync(ct)` lists all `FundAccount`s, writes one `BalanceSnapshot(Reason =
  Scheduled)` per account at its current balance — no mutation to the balance itself, purely a
  point-in-time record (mirrors reconciliation's per-item try/catch pattern: one account's failure
  doesn't abort the rest).
- Verify: `ScheduledBalanceSnapshotServiceTests` (writes one snapshot per fund account; one
  account's failure doesn't abort the rest) -> `test-fast.sh Application`.

### 18.2 — Background service + config
- Files: `Infrastructure/Snapshots/ScheduledBalanceSnapshotBackgroundService.cs`, `Program.cs`,
  `appsettings.json` (`"BalanceSnapshot:IntervalSeconds"`, default 3600 — hourly, no source doc
  specifies a value; ratify during task execution if the user wants a different default).
- Verify: `check.sh` full solution.

### 18.3 — Integration test
- Files: `tests/.../ScheduledBalanceSnapshotIntegrationTests.cs`.
- Verify: `test-full.sh`; `check.sh` full solution.

---

## Ticket 19 — Observability completion (structured logging audit)

Scope per 2026-07-18 grilling: structured-logging audit only — no OpenTelemetry/metrics/tracing
stack, matches the repo's current no-OTel state. Blocked by: none.

### 19.1 — Audit
- No files changed — an `Explore`/read-only pass across every handler and background service
  (`Application/**/*Handler.cs`, `Infrastructure/**/*BackgroundService.cs`) checking: does every
  catch-and-continue path (reconciliation §7.4, dead-letter dispatch, snapshot loop) log at
  `Error` with the correlation id and enough structured fields (entity id, provider reference) to
  find it later without re-reading code. Produce a short gap list, not a doc.
- Verify: no verification script — this task's output is the gap list feeding 19.2.

### 19.2 — Fix gaps found
- Files: whichever handlers/services 19.1 flags.
- Change: add missing structured log fields/levels only — no new logging *infrastructure*, this
  repo already uses `ILogger<T>` throughout.
- Verify: `check.sh` full solution; spot-check via `test-full.sh`'s existing suite (no dedicated
  new test file expected — logging isn't typically asserted in tests here; if 19.1 finds a gap
  serious enough to warrant a regression test, e.g. a swallowed exception with *no* log at all,
  add one for that specific case only).

---

## Ticket 20 — Full list-endpoint pagination/filtering

Only `TransactionListFilter` currently carries `Page`/`PageSize` (ticket 08.2). Every other
`List*` query (`DepositAddresses`, `Recipients`, `Transfers`, `Redemptions`,
`LinkedBankAccounts`) returns everything unpaged. Blocked by: none.

### 20.1 — Shared pagination record
- Files: `Application/Shared/PageRequest.cs` (`record PageRequest(int Page = 1, int PageSize =
  20)`, mirrors `TransactionListFilter`'s existing field names/defaults — don't invent a second
  shape).
- Verify: `check.sh Application`.

### 20.2 — Extend each `List*Query`/handler/repository
- Files: `Application/Ledger/{ListDepositAddressesQuery,ListRecipientsQuery,ListTransfersQuery,
  ListRedemptionsQuery,ListLinkedBankAccountsQuery}.cs` (add `PageRequest` param),
  corresponding repository interfaces + `Infrastructure/Persistence/*Repository.cs`
  implementations (add `.Skip().Take()`).
- Verify: one test per query asserting page 2 returns the next slice, `PageSize` bounds the count
  -> `test-fast.sh Application`.

### 20.3 — Api query-string binding
- Files: corresponding controllers under `Api/Ledger/` (bind `?page=&pageSize=` to
  `PageRequest`).
- Verify: Api integration tests (one representative endpoint's pagination round-trip, not all five
  redundantly) -> `test-full.sh`; `contract.sh`; `check.sh` full solution.

---

## Execution protocol (per writing-plans skill)

Same as Phase 1: dispatch `task-executor` per sub-task, two-stage review (spec compliance against
the stated verification, then code-quality against CLAUDE.md invariants), only then mark done and
move to the next. Migrations shown to the user before `schema.sh apply` (none currently expected
in this plan — flag here if a task turns out to need one, e.g. if dead-letter replay needs a new
`ReplayedAtUtc` column).
