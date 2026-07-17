---
title: Sequence DepositReconciliationPLan.md into the roadmap
label: wayfinder:grilling
status: closed
assignee: danish
blocked_by: []
parent: map.md
---

## Resolution

Checked `DepositReconciliationPLan.md`'s own structure: 7 tasks, each already ending
its own commit (none oversized — chunk-splitting policy from
[Decide chunk-splitting policy for oversized Phase 1 tasks](002-chunk-splitting-policy.md)
does not apply here). Checked the two claimed cross-doc dependencies directly:

- Task 6's `NotificationDispatchBackgroundService` reference (line 912, "patterned
  directly on") is a **style precedent to copy**, not a compile/runtime dependency —
  doesn't block anything.
- Task 6's DI wiring (line 968) registers `MockSubAccountGateway` as
  `ISubAccountGateway` — this **is** a hard dependency on Phase 1 Task 5's nested
  mock-provider chunk (Task 6 in `Phase_1_Feature_Slices.md`, `MockSubAccountGateway`
  itself).

Decision: interleave the reconciliation Tasks 1-7 as their own chunk group
immediately after Phase 1's mock-provider chunk lands (`MockSubAccountGateway`
available), rather than appending them after Phase 1 Task 14 per PRD §15.2's Phase-2
labeling — nothing technically blocks it running earlier, and mock-mode demoability
is the point. The final roadmap document's sequence:

```
... Task 5 core (webhook pipeline)
... Task 5 nested / Task 6 (mock provider) <- unlocks reconciliation
>>> Reconciliation Tasks 1-7 <<<
... Task 7 (deposit address gen)
... Task 8 (ledger) ...
... Task 14 (demo e2e)
```

## Question

PRD §15.2 places the reconciliation job in **Phase 2** ("must ship before any
real client money moves... against mock mode it is testable in Phase 2"), but
`docs/DepositReconciliationPLan.md` is already a complete, ready-to-execute
7-task implementation plan sitting alongside the Phase 1 doc. Its own Task 6
depends on `NotificationDispatchBackgroundService` (a Phase 1 Task 13
artifact) and its mock ledger depends on `MockSubAccountGateway` (Phase 1
Task 6) — so it's technically buildable only after specific Phase 1 tasks
land, not strictly "after all of Phase 1."

Decide where it slots into the roadmap's chunk sequence: appended after
Phase 1 Task 14 (demo-script E2E) as a distinct Phase-2 chunk, or interleaved
right after its true dependencies (Task 6 + Task 13) land, since nothing
blocks it from running earlier than "after Phase 1 finishes" except doc
labeling. This determines whether the roadmap treats it as chunk 15 or
folds it in earlier.
