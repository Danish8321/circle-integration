# TreasuryServiceOrchestrator — tech layer

@CONTEXT.md

All four tiers implemented (Domain/Application/Infrastructure/Api), invariants below enforced
by `NetArchTest` fitness tests (`tests/TreasuryServiceOrchestrator.ArchitectureTests`). See
`.scratch/architecture-review/spec.md` for the latest full review. Rules below come from
`docs/README.md` and the feature files it indexes under `docs/features/` (B0.5
module-boundaries decision, 2026-07-16, see `docs/adr/0001-module-boundaries.md`). Items
marked **(assumed)** aren't nailed down in docs — tighten/replace as they firm up.

## Stack

.NET 10 / C# 14, ASP.NET Core **Controllers** (`[ApiController]`, see the Api-tier design in
each `docs/features/*.md` file), EF Core 10, SQL Server (LocalDB dev). No Angular (or any)
client in this repo — API-only.

## Tiers & layout

This is plain **Clean Architecture**. One hard rule: dependencies point inward
(Domain ← Application ← Infrastructure, Api wires all). Every tier files by **kind**
(controller, handler, port, DTO, validator), not by business module or use-case
folder — there is no second axis to learn. New here? Read `ARCHITECTURE.md` at the
repo root: it has the "where does X live" map and one request traced end to end.

Full "must not" rules per tier are path-scoped and auto-load from `.claude/rules/{domain,application,infrastructure,api}.md` — that's the single source, don't restate them here.

| Tier | Path | Owns | Rules |
|---|---|---|---|
| Domain | `src/TreasuryServiceOrchestrator.Domain/` (flat — one file per entity/value object/enum) | Entities, value objects (`Money`), domain events, invariants. | `.claude/rules/domain.md` |
| Application | `src/TreasuryServiceOrchestrator.Application/{Handlers,Ports,Dtos,Validators,Services,Exceptions}/` | Use-case handlers (`ICommandHandler`/`IQueryHandler`) in `Handlers/`, ports (interfaces) in `Ports/`, commands/queries/results in `Dtos/`, FluentValidation in `Validators/`, cross-cutting app logic (idempotency, tenant scope resolution, mappers) in `Services/`. | `.claude/rules/application.md` |
| Infrastructure | `src/TreasuryServiceOrchestrator.Infrastructure/{Persistence,Providers/Circle,Mocks,Webhooks,Notifications,Reconciliation,Snapshots,Migrations}/` | `DbContext`, EF configs/migrations, port implementations (repositories, `CircleSubAccountGateway`/`CircleMintGateway`), `HttpClient`-backed gateways. | `.claude/rules/infrastructure.md` |
| Api | `src/TreasuryServiceOrchestrator.Api/{Controllers,Dtos,Validators,Middleware,DependencyInjection}/` | Controllers (thin — dispatch to handler, return result), middleware, DI wiring (`Program.cs` + `DependencyInjection/`), request/response contracts. | `.claude/rules/api.md` |

Clean dependency rule: Domain ← Application ← Infrastructure, and Api wires all three. Arrows
point inward only; nothing inward-of a tier may reference outward.

No use-case folders, no module folders. A new use-case is a `Command`/`Query` in `Dtos/`, a
`Handler` in `Handlers/`, a `Result` in `Dtos/` — filenames (not folders) disambiguate one
use-case from another. Earlier docs describing "VSA / Vertical Slice" or module sub-namespaces
(`Compliance`/`Ledger`/`Webhooks`/`Admin`) describe a structure this repo no longer uses; see
`docs/adr/0001-module-boundaries.md` for the superseding decision.

## The seam

- OpenAPI document (`docs/openapi/openapi.json`, emitted by `contract.sh`) is the contract of
  record for the Api tier — response/request DTOs must match it exactly.
- No client project exists in this repo. `contract.sh`'s client-regen step stays a no-op stub;
  don't add a Angular/TS client here unless the user asks.

## Invariants

1. No repository abstraction over `DbContext` — Infrastructure ports wrap use-case-shaped
   queries, not a generic `IRepository<T>`. (architecture decision, B0.5)
2. `TimeProvider`, never `DateTime.Now`/`DateTime.UtcNow` directly. (standard Clean practice —
   testability of the lifecycle/reconciliation timers, see `docs/features/05-reliability-and-error-handling.md`)
3. `IHttpClientFactory`, never `new HttpClient()` — Circle gateways need pooled/resilient
   handlers. (provider resilience: timeouts, retry+backoff, circuit breaker — see
   `docs/features/05-reliability-and-error-handling.md`)
4. `CancellationToken` threaded through every async path; every handler signature is
   `HandleAsync(TCmd cmd, CancellationToken ct = default)`. (global constraint — xUnit1051 is a
   build error)
5. Domain entities never leak past the Application boundary into an API response — Api maps
   Application DTOs, never serializes a Domain/EF entity. (Clean layering + stable RFC 7807
   error contract implies stable response shapes, not leaky entities — see
   `docs/features/05-reliability-and-error-handling.md`)
6. Every endpoint has a validation filter — no controller hand-rolls validation inline.
   (FluentValidation, RFC 7807 `ProblemDetails` only error contract, controllers must not catch
   domain exceptions themselves — see `docs/features/05-reliability-and-error-handling.md`)
7. Tenant identity (`ClientCompanyId`) always comes from `ICallerContext`/`ITenantContext`,
   never a route or body parameter; cross-tenant access must be structurally impossible at the
   data-access layer. (see `docs/features/01-tenancy-and-authorization.md`)
8. Admin never impersonates a tenant — authenticates as itself, names target scope explicitly;
   all-tenant access is itself audited. (see `docs/features/01-tenancy-and-authorization.md`)
9. Mock mode must be structurally impossible to enable in Production — hard environment check
   at startup, not config alone. (see `docs/features/02-mock-mode.md`)
10. `Money(decimal Amount, string CurrencyCode)` is the only monetary type crossing the
    Domain/Application boundary; no floating-point money anywhere. (global constraint, see
    `docs/features/04-ledger-and-balances.md`)
11. Every mutating handler follows reserve → gateway/state-transition → complete, two
    `SaveChangesAsync` calls, idempotency key required on every mutating consumer operation and
    forwarded to the provider on money-moving calls. (see
    `docs/features/05-reliability-and-error-handling.md`)
12. Outbound transfer commands must not carry Travel Rule originator name/address fields on
    `POST /v1/businessAccount/transfers` — no such request field exists; Travel Rule is
    satisfied structurally via account-on-file identity + recipient verification. (verified
    against live Circle docs 2026-07-16, re-verified 2026-07-17 — see
    `docs/features/10-outbound-transfers-and-recipients.md` and the `circle_travel_rule_fix`
    memory)

## Testing strategy per tier

| Tier | Framework | What is tested | What is NOT |
|---|---|---|---|
| Domain | xUnit v3 (Microsoft Testing Platform) | Entity invariants, value objects, state-machine transitions (§3.2), pure domain logic. | Persistence, HTTP, DI wiring. |
| Application | xUnit v3, Moq (mock ports), FluentAssertions | Handler orchestration against mocked ports, validation rules, idempotency branching. | Real `DbContext`, real HTTP calls to Circle. |
| Api | WebApplicationFactory + Testcontainers (real SQL Server) | Full request pipeline: middleware, tenant scoping, controller → handler → real DB round trip, RFC 7807 error shapes. | In-memory EF provider fakes — forbidden; Testcontainers only, so EF behavior (collation, constraints) is real. |

## Scripts

- `check.sh [$FILE]` — build (warnings-as-errors + analyzers) the owning `.csproj`; non-zero = blocked.
- `test-fast.sh [$SCOPE]` — unit tests only, under 60s.
- `test-full.sh` — integration + e2e against Testcontainers SQL Server, may be slow.
- `contract.sh` — emit OpenAPI doc (build-time, in-process via `Microsoft.Extensions.ApiDescription.Server`; no running server/loopback), regenerate Angular client, diff generated dir (client step stubbed until Angular project exists).
- `e2e.sh [$SPEC]` — Playwright against `run.sh` (stubbed until a client/flow exists).
- `format.sh $FILE` — `dotnet format` in place, always exits 0.
- `run.sh` — start the full stack locally (API only today; extend once Infrastructure needs a real DB).
- `schema.sh new|apply|verify` — `dotnet ef migrations add` / `database update` / idempotent script (fails until a `DbContext` exists — expected, not a bug).

## Agent skills

### Issue tracker

Local markdown under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at repo root. See `docs/agents/domain.md`.
