---
title: Specify the missing Task 0 (solution scaffold)
label: wayfinder:grilling
status: closed
assignee: danish
blocked_by: []
parent: map.md
---

## Resolution

**Note (2026-07-17):** `docs/Task_0_Scaffold.md` (referenced below) was deleted as stale —
the scaffold it specified is long since implemented and its stub interface names
(`ICircleSubAccountGateway`/`ICircleMintGateway`/`ITenantContext`) never shipped; actual
code uses `ISubAccountGateway`/`IStablecoinGateway` and no `ITenantContext` (see
`Phase_1_Feature_Slices.md`'s "Doc drift" corrections item 7). This ticket stays closed as
a historical record of the decision to scaffold at all — don't resurrect the deleted file
or its interface names.

Repo already has a raw `dotnet new`-scaffolded solution (checked live: 4 src + 3 test
projects, CPM, `Directory.Build.props`, `TreasuryServiceOrchestrator.slnx`) — unmodified
template output, not yet hardened. Task 0 spec written to `docs/Task_0_Scaffold.md`
(new file, Files/Interfaces/Steps format matching Phase_1_Feature_Slices.md): strip
template cruft (WeatherForecast*), empty `DbContext`, empty `ICircleSubAccountGateway`/
`ICircleMintGateway` port stubs + Infrastructure impls via `IHttpClientFactory` typed
clients, `ICallerContext`/`ITenantContext` port shapes (no impl — Task 1/2 fill them),
`TimeProvider` DI registration, Production mock-mode guard, and `LayeringTests`
(NetArchTest.Rules) encoding this repo's tier `must-not` rules as executable assertions.
Explicitly out of scope: entities/migrations, gateway method bodies, mock implementations,
idempotency middleware — each deferred to the task that first needs it.

## Question

`Phase_1_Feature_Slices.md` Task 1 assumes a pre-existing solution structure —
`TreasuryServiceOrchestrator.{Domain,Application,Infrastructure,Api}` projects,
a `TreasuryServiceOrchestratorDbContext`, baseline `CircleSubAccountGateway`/
`CircleMintGateway` stub implementations, `Directory.Build.props`/
`Directory.Packages.props`, xUnit v3 test projects — none of which exist yet
and none of which any doc specifies how to create.

What exactly does "Task 0" need to produce so Task 1 can start cleanly? Scope:
project layout, empty-but-compiling Clean/Onion projects, CPM package file,
`net10.0`/`Nullable=enable`/`TreatWarningsAsErrors=true` build props, stub
gateway interfaces+empty implementations Task 1 onward extend, and whatever
minimal `Program.cs`/test-host wiring later tasks assume exists ("existing"
in their Consumes lists). Resolve by writing this as an actual Task 0 section,
same format as Phase_1_Feature_Slices.md's existing tasks (Files/Interfaces/
Steps), to be inserted at the top of that doc — or as a new file if that reads
better structurally.
