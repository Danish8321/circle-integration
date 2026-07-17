---
title: Decide chunk-splitting policy for oversized Phase 1 tasks
label: wayfinder:grilling
status: closed
assignee: danish
blocked_by: []
parent: map.md
---

## Resolution

Checked actual step structure (`^\- \[ \] \*\*Step` line scan): Task 5 already splits
naturally — webhook pipeline core (Steps 1-12) and the nested Task 6 mock provider
(Steps 1-17) each end their own commit already, so no policy needed there. Task 8
(Ledger, 16 steps/1 commit), Task 10 (Transfers, 31 steps/1 commit), and Task 11
(Redemption, 32 steps/1 commit) are the real single-commit monoliths needing a cut.

**Split by vertical mini-slice**, not by architectural layer — each chunk is a
complete thin slice (its own domain/repo/handler/test), not a "do all Domain, then
all Infra, then all Api" pass. Example, Task 10 (Transfers):
- Chunk A (Steps 1-9): `Transfer` domain entity, `ITransferRepository`, gateway
  DTO/interface extension + stub.
- Chunk B (Steps 10-15): `CreateTransferCommand` + handler + mock gateway impl.
- Chunk C (Steps 16-19): List/Get query handlers.
- Chunk D (Steps 20-27): status webhook processing (`ProcessTransferStatusCommand`,
  `TransfersWebhookTopicProcessor`).
- Chunk E (Steps 28-31): repo impl, `DbContext` mapping, DI, controller, migration,
  integration test.

Same shape applies to Task 8 and Task 11 — cut at each point where a handler's own
unit tests turn green before the next concern starts.

**Each chunk gets its own commit-gated checkpoint** — its own `dotnet build`/
`dotnet test` green gate before the next chunk starts, not one final commit for
the whole original task. Matches this repo's CLAUDE.md "small atomic commits" and
lets a `task-executor` session be interrupted/resumed between chunks without a
large uncommitted diff sitting in the tree.

This is the template the roadmap document (not yet written — depends on all three
tickets on this map) applies uniformly wherever a Phase 1 task exceeds ~600 lines.

## Question

Four of the 14 Phase_1_Feature_Slices.md tasks are far larger than the rest —
Task 5 (webhook pipeline, ~1229 lines), Task 8 (ledger, ~1046 lines), Task 10
(outbound transfers, ~1220 lines), Task 11 (redemption rework, ~1544 lines) —
versus the median task at 300-600 lines. "Smaller achievable chunks" implies
these four don't stay single units in the roadmap.

Decide: split policy for these four (e.g. by Step-group boundaries already
present in each task — domain entity, then handler, then webhook processor,
then controller, then integration test — or some other cut), and whether the
split chunks get their own commit-gated checkpoints (each with its own
`dotnet build`/`dotnet test` green gate) or stay one commit per original task
with just planning-level sub-chunks for tracking. This decision sets the
template the roadmap document applies uniformly to all four.
