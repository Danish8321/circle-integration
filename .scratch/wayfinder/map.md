---
label: wayfinder:map
status: closed
---

# Map: Phase 1 execution roadmap

**Note (2026-07-17)**: `docs/Phase_1_Feature_Slices.md`, `docs/PRD.md`,
`docs/DepositReconciliationPLan.md`, `docs/Phase_3_Circle_Integration_Plan.md` — all referenced
below — were deleted this same day and replaced by `docs/README.md` + `docs/features/*.md`. This
map is a closed historical record (status above); left as-written rather than rewritten, since it
describes decisions made when those files were current. Do not use it to look up current doc
paths — use `docs/README.md`.

## Destination

A sequenced, dependency-ordered roadmap that splits `docs/Phase_1_Feature_Slices.md`'s
14 tasks and `docs/DepositReconciliationPLan.md` into small, shippable chunks —
including the missing scaffold step neither doc currently specifies — ready to
hand to `task-executor` sessions one chunk at a time. This map produces the
roadmap document; it does not execute the build.

## Notes

- Repo has a raw `dotnet new`-scaffolded solution (4 src + 3 test projects, CPM,
  `Directory.Build.props`, `.slnx`) but no feature code — unmodified template
  output, hardened by `docs/Task_0_Scaffold.md` (see Decisions so far). Docs are
  otherwise decision-complete (see 2026-07-16 doc-verification pass —
  PRD/Phase_1/circle-mint-docs cross-checked against live `developers.circle.com`,
  two corrections applied and committed).
- `Phase_1_Feature_Slices.md` presumes a pre-existing solution baseline
  (`CircleSubAccountGateway (Task-0 baseline)`, line 2733) that was never
  written down anywhere — first thing this map must produce.
- When executing chunks later (out of this map's scope), use `vertical-slice`,
  `writing-plans`, `tdd` skills per this repo's `CLAUDE.md`.
- Tracker: local markdown under `.scratch/wayfinder/` (no external tracker
  configured for this repo).

## Decisions so far

- [Specify the missing Task 0 (solution scaffold)](tickets/001-scaffold-task.md) — spec
  written to `docs/Task_0_Scaffold.md`: hardens the existing raw-scaffolded solution
  (strip WeatherForecast template, empty DbContext, gateway port stubs via
  `IHttpClientFactory`, caller/tenant port shapes, `TimeProvider` DI, Production
  mock-mode guard, NetArchTest `LayeringTests`); entities/migrations/gateway bodies/
  mock impls/idempotency middleware deferred to the tasks that first need them.
- [Decide chunk-splitting policy for oversized Phase 1 tasks](tickets/002-chunk-splitting-policy.md) —
  Task 5 already self-splits (two commits, no action needed); Task 8/10/11 split by
  vertical mini-slice (not by layer), each chunk its own commit-gated build/test
  checkpoint, not one final commit per original task.
- [Sequence DepositReconciliationPLan.md into the roadmap](tickets/003-reconciliation-sequencing.md) —
  its 7 tasks already self-split (no policy needed); interleave as its own chunk
  group right after Phase 1's mock-provider chunk lands (hard dependency via
  `MockSubAccountGateway`), not after Phase 1 Task 14 per PRD §15.2's Phase-2 label.

## Not yet specified

(none — all three tickets resolved; nothing left to decide before the roadmap
document itself gets written)

## Out of scope

- Actually writing or executing any code chunk. This map stops at a reviewable
  roadmap; building it is a separate, later effort.
- Re-verifying doc content against live Circle docs — already done and
  committed (see `docs/PRD.md`, `docs/Phase_1_Feature_Slices.md`,
  `docs/circle-mint-docs/` git history for 2026-07-16 corrections).
