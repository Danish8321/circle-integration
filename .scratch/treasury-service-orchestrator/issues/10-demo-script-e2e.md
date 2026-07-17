Status: open

Source: `docs/README.md` §5 (demo script) (old source `docs/Phase_1_Feature_Slices.md` Task 14,
deleted 2026-07-17 — superseded by the per-feature doc restructure).
Blocked by: 09-notifications-outbox.

## Scope

Single integration test walking the PRD §15.1 demo script start to finish — the terminal
acceptance gate for Phase 1. Asserts every clause: admin creates a sub-account → screening
`ACCEPTED` (and a second one `REJECTED` + resubmitted) → generate deposit address → simulated
deposit credits ledger and balance rises → register recipient, simulated approval → outbound
transfer completes → redemption completes showing gross/fees/net → tenant sees only its own
data; admin sees all sub-accounts and master summary → every step visible in transactions,
balance history, and audit records → each state change also arrives as an internal-notification
callback at the stub receiver.

If this test is green, Phase 1 is done. If red, Phase 1 is not done — no separate acceptance
step exists beyond this file.

## Files

- New: `tests/TreasuryServiceOrchestrator.IntegrationTests/DemoScriptEndToEndTests.cs`.

## Definition of done

- Test consumes every controller route/DTO shape from tickets 01-09 exactly as shipped (not as
  originally drafted in the doc — reconcile against actual signatures, since design-pass
  corrections may have changed shapes along the way).
- `test-full.sh` green (Testcontainers, full pipeline).
- `check.sh` clean.
- No further Phase 1 work is "done" until this ticket is green — treat any earlier ticket marked
  done-but-this-test-fails as not actually done, per the doc's own framing.

## Comments
