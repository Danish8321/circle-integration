Status: DONE 2026-07-19 (test-fast 404/404, migration applied to dev LocalDB)

## Shipped
- S1: `IdempotencyOutcome` (Started/Replay/InFlightRetry) + `IIdempotencyService.TryBeginAsync`/
  `CompleteAsync`; `IdempotencyRecord` gained Status/nullable ResultJson/CompletedAtUtc. No lease.
- S2: `IdempotencyExecutor` reserve-first (reserve→SaveChanges #1→work→complete→SaveChanges #2).
- S3: `LedgerPostingService.PostAsync(..., deferCommit)`; CreateTransfer + ProcessDeposit defer so
  the ledger posting commits atomically in SaveChanges #2. ProcessPayoutStatus (webhook, not
  executor-wrapped) unchanged — its own commit; F6-for-redemption-debit still to re-check (below).
- S4: migration `20260719064139_AddIdempotencyReservationState`; generated `Status` default was
  `""`, corrected to `"Completed"` (pre-migration rows are terminal) before apply.
- S5: new tests — reserve-committed-before-gateway ordering + InFlightRetry re-drive (one commit).

## Open follow-ups
- **INV11 wording**: handlers that keep their own pre-gateway domain-reserve save (CreateSubAccount,
  RegisterRecipient, Resubmit, GenerateDepositAddress) now do THREE commits (executor reserve +
  their domain reserve + completion). Still correct; the "two SaveChanges" invariant text now
  understates these. Decide: reword INV11 to "≥2, reserve before gateway", or drop the redundant
  executor reserve save when the handler self-reserves. **Needs a governance call — not silently
  edited.**
- ProcessPayoutStatus redemption debit (webhook path) atomicity vs F6 — sampled, not re-driven.
- Concurrency: two same-key requests both getting `Started` race on the unique index at
  SaveChanges #1 → `DbUpdateException` (same window as before, fires earlier). No lease in v1.
- IdempotencyService itself has no integration test (Infra = Testcontainers/test-full, not added).

---

Status: spec — awaiting review
Priority: P1 — closes audit finding F6 (ticket 22). Money-path atomicity.
Type: refactor / robustness

Source: audit F6 (`22-architecture-audit-2026-07-19.md`). Design call 2026-07-19: fix shape =
**Option C, persisted reservation** ("best per Fintech" — never call a money-moving provider
without a durable local record first; a crash right after the gateway call must leave a
recoverable in-flight row, not a silent gap only Circle knows about).

## Problem (restated)

INV11 intends `reserve → gateway → complete`, two `SaveChangesAsync`: #1 = persisted reservation
BEFORE the gateway, #2 = complete AFTER. Implementation drifted: "reserve" is only a cache read
(`IdempotencyService.TryGetCachedResultJsonAsync`), persists nothing; both saves land after the
gateway (ledger posting = save #1 inside `PostAsync`; idempotency row + aggregate = save #2 inside
`IdempotencyExecutor`). Crash between them leaves the ledger `Transaction` committed (unique
`ProviderReferenceId`) but the aggregate + idempotency row absent; retry re-posts the same
`ProviderReferenceId` → unique-index violation → 500 forever. Operation un-completable.

## Target shape

Two saves, correctly placed:
- **#1 (pre-gateway) — reserve:** insert an `IdempotencyRecord` in state `InProgress` (RequestHash
  set, ResultJson null). The existing unique `(TenantId, IdempotencyKey)` index is the concurrency
  guard.
- **work:** gateway call → stage ledger posting → stage aggregate.
- **#2 (post-gateway) — complete (ONE atomic commit):** ledger posting + aggregate + flip
  reservation `InProgress → Completed` with ResultJson, all in a single `SaveChangesAsync`.

Crash matrix:
- before #1 → nothing local; client retry is fresh.
- after #1, before gateway → `InProgress`, no money moved; retry re-drives, gateway called once.
- after gateway, before #2 → `InProgress`, money moved at Circle, ledger NOT committed; retry
  re-drives → gateway re-called → Circle dedups (same idempotency key → same transfer id) → ledger
  posts fresh (no prior Transaction row → unique index OK) → completes. **F6 scenario, now
  recoverable.**
- after #2 → `Completed` + ResultJson; retry returns cached result.

## Sections (review each before coding the next)

### S1 — Idempotency state machine (Application port + Infra) — REVIEW FIRST
- `IdempotencyRecord`: add `Status` (`InProgress|Completed`), make `ResultJson` nullable,
  add `CompletedAtUtc?`.
- `IIdempotencyService` gains:
  - `TryBeginAsync(tenant, key, requestHash, ct) → IdempotencyOutcome` where outcome is one of:
    `Started` (new InProgress row staged — caller must SaveChanges #1), `Replay(resultJson)`
    (existing Completed, same hash), `InFlightRetry` (existing InProgress, same hash → caller
    re-drives work), or throws on hash mismatch (key reused w/ different payload — existing rule).
  - `CompleteAsync(tenant, key, resultJson, ct)` — flip InProgress→Completed + ResultJson (staged,
    committed by caller's SaveChanges #2).
  - Drop `TryGetCachedResultJsonAsync` / `StoreResultAsync` once callers migrate.
- **Decision to confirm:** on `InFlightRetry` do we re-drive unconditionally (chosen — needed for
  the after-gateway crash recovery), accepting one duplicate gateway call that Circle dedups?
  Yes per the crash matrix. No time-based lease in v1 (documented as follow-up if concurrent
  in-flight duplicates become a concern).

### S2 — `IdempotencyExecutor` restructure
Reserve-first flow: `TryBeginAsync` → if Replay return; SaveChanges #1 (Started) → `work()` →
`CompleteAsync` → SaveChanges #2. `work()` must NOT call its own SaveChanges. All 6 callers get
this behavior; non-money callers are strictly safer (durable in-flight marker).

### S3 — `LedgerPostingService.PostAsync` defer-commit
Add an overload/flag so money-path callers stage the posting without an internal SaveChanges; the
executor's SaveChanges #2 commits ledger + aggregate + completion atomically. Non-executor caller
`ProcessPayoutStatusCommandHandler` (webhook redemption debit, not idempotency-wrapped) keeps its
own commit but must be re-checked against F6 separately — its dedup guard is the redeem-request
state, note in S3.

### S4 — Schema migration (`schema.sh new`)
New columns on `IdempotencyRecords` (Status, nullable ResultJson, CompletedAtUtc). **Read the
generated migration before `schema.sh apply`** — ResultJson non-null→null is a widening, safe;
Status needs a default (`Completed` backfill for any existing rows, since all pre-migration rows
are terminal). No rename, no drop.

### S5 — Tests + INV11 doc note
Application tests: reserve-before-gateway ordering, replay path, in-flight re-drive path, atomic
completion. Update the CLAUDE.md INV11 gloss if wording implies the old placement (it says "two
SaveChanges" — still true, placement now matches intent; likely no text change needed, confirm).

## Not in scope
F7 (structural tenant isolation) — separate ticket. F2/F3/F8 — separate.
