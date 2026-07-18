# Phase 4 Implementation Plan

Status: draft, awaiting approval. Not blocking Phase 3 — these are gaps surfaced by the
2026-07-18 doc/ticket audit (`docs/README.md` §7 "Known open items") that had no ticket anywhere.
None require real Circle sandbox credentials or new external infra.

## Scope

Four items, none touching Phase 3's Circle-integration work:

---

## Ticket 26 — `LedgerPostingService` shape confirmation + currency-default question

`docs/README.md` item 04: `LedgerPostingService`'s exact shape was synthesized, not sourced from
a confirmed spec — flagged for review. Separately, `GetCurrentBalanceQueryHandler` defaults to
`Money.Zero("USD")` when no balance row exists, but funded accounts operate in `USDC` — open
product question on what an unfunded account's balance currency should read as.

### 26.1 — Confirm `LedgerPostingService` shape against source docs
- Files: `Application/Ledger/*/LedgerPostingService.cs` (read current shape), cross-check against
  `docs/features/04-ledger-and-balances.md`.
- Change: either confirm current shape matches the doc (no code change, just close the flag with
  a note), or fix a genuine mismatch if one's found.
- Verify: `test-fast.sh Application` if code changes; otherwise no script — a documented
  confirmation is sufficient.

### 26.2 — Resolve `Money.Zero("USD")` vs `USDC` default
- Files: `Application/Ledger/*/GetCurrentBalanceQueryHandler.cs`.
- Change: decide (grill if genuinely ambiguous) whether an unfunded account's balance should
  read `Money.Zero("USDC")` (matches the funded-state currency) or stay `USD` (matches invariant
  10's cross-boundary type, independent of stablecoin denomination) — apply the decision.
- Verify: unit test asserting the chosen default -> `test-fast.sh Application`.

---

## Ticket 27 — Correlation id echoed in HTTP responses

`docs/README.md` item 06: correlation id (`HttpContext.TraceIdentifier`) is used internally
(e.g. `DeadLetterController`'s replay commands) but never returned to the caller in the response,
making client-side support correlation harder.

### 27.1 — Echo correlation id
- Files: likely a response header added centrally (middleware or `ProblemDetails` factory) rather
  than per-controller — check existing RFC 7807 error-shape wiring first (CLAUDE.md invariant 6)
  for the right seam.
- Change: every response (success and error) carries the correlation id, e.g. as an
  `X-Correlation-Id` header echoing `HttpContext.TraceIdentifier`.
- Verify: Api integration test asserting the header is present on both a success and an error
  response -> `test-full.sh`.

---

## Ticket 28 — Fault-injection test for outbox/state-change atomicity

`docs/README.md` item 13: existing integration test for the reserve -> gateway -> complete /
outbox pattern (invariant 11) is happy-path only. No test proves the same-transaction guarantee
holds when `SaveChangesAsync` fails mid-way (e.g. between the two calls the two-`SaveChangesAsync`
pattern requires).

### 28.1 — Fault-injection test
- Files: new test in the Api-tier Testcontainers suite (find the existing happy-path test for
  this pattern first, e.g. around `ProcessDepositCommandHandler` or wherever invariant 11's
  two-`SaveChangesAsync` sequence is exercised).
- Change: inject a failure between the two `SaveChangesAsync` calls (test double or a
  Testcontainers-compatible fault-injection technique — check what's already used elsewhere in
  this suite) and assert no partial state persists (outbox entry and state transition are both
  absent, not just one).
- Verify: `test-full.sh` — new test passes, proving atomicity; a deliberately-reverted fix should
  make it fail (sanity-check locally, not committed).

---

## Ticket 29 — Stub notification receiver bypass-path review

`docs/README.md` item 02.2: `CallerIdentityMiddleware` now bypasses `ClientCompanyId` scoping for
`/v1/webhooks/circle` (PRD §10 item 7). Ticket 13's internal notification-outbox stub receiver
may need the same bypass treatment — currently unconfirmed either way.

### 29.1 — Confirm or fix the stub receiver's scoping
- Files: the notification stub receiver from ticket 13 (find via `Application/Webhooks` or
  wherever `NotificationOutboxEntry` delivery lands — check `HttpNotificationSender.cs` and its
  receiving side).
- Change: confirm whether the receiver is internal-only (no `ClientCompanyId` scoping needed,
  same reasoning as an internal service-to-service call) or externally reachable (needs the same
  bypass as the webhook endpoint) — apply whichever the trace shows is correct.
- Verify: no script if confirmation only; `test-full.sh` if a scoping fix is needed.

---

## Execution protocol

Same as Phase 1/2/3: `task-executor` per sub-task, two-stage review, verification before marking
done. No external blockers — all four tickets can start immediately, independent of Phase 3's
credential/AWS blockers.
