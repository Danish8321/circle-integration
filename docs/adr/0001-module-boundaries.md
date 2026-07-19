# ADR 0001: Clean Architecture only — no module or use-case folders

**Status:** Accepted (2026-07-19). Supersedes the original 2026-07-16 decision below.

> **Plain-language summary.** This is **Clean Architecture** — the only hard rule is
> the dependency direction: Domain ← Application ← Infrastructure, Api wires all.
> Every tier files by **kind of thing** (controller, handler, port, DTO, validator),
> not by business area and not by use-case. See `ARCHITECTURE.md` at the repo root
> for the full folder map and one fully-traced request.

## Decision (2026-07-19, current)

Drop the module axis (`Compliance`/`Ledger`/`Webhooks`/`Admin`/`Shared`) and the
use-case-folder axis entirely, in every tier. Group by kind instead:

| Tier | Folders |
|---|---|
| Domain | *(flat — no subfolders)* |
| Application | `Handlers/`, `Ports/`, `Dtos/`, `Validators/`, `Services/`, `Exceptions/` |
| Infrastructure | `Persistence/`, `Providers/Circle/`, `Mocks/`, `Webhooks/`, `Notifications/`, `Reconciliation/`, `Snapshots/`, `Migrations/` (flat — one per `DbContext`, an EF Core constraint) |
| Api | `Controllers/`, `Dtos/`, `Validators/`, `Middleware/` |

Rationale: the original module+use-case hybrid was reported confusing to traverse —
three overlapping axes (layer, module, use-case) meant every new file needed three
decisions instead of one. Grouping by kind gives one axis per tier: "what kind of
thing am I writing" is always answerable without also picking a business area.
Filenames (not folders) disambiguate one use-case from another
(`CreateSubAccountCommand.cs` vs `CreateTransferCommand.cs` both sit in `Dtos/`).

## Consequences

- New handlers go in `Application/Handlers/`, new ports in `Application/Ports/`, new
  command/query/result types in `Application/Dtos/`, new validators in
  `Application/Validators/`. Cross-cutting app logic that isn't a handler
  (idempotency execution, tenant scope resolution, status mappers, background-service
  option classes) goes in `Application/Services/`.
- New controllers go in `Api/Controllers/`, new request/response records in
  `Api/Dtos/`, new request validators in `Api/Validators/`.
- New entities/value objects go directly in `Domain/` (flat).
- New repositories/gateways go under the matching Infrastructure technical-concern
  folder (`Persistence/` for EF repos, `Providers/Circle/` for real gateways,
  `Mocks/` for mock/fake implementations).
- Any implementation plan or doc still referencing `<Module>/<UseCase>/` folders is
  stale — reconcile against this ADR before scaffolding.

---

## Original decision (2026-07-16 — superseded, kept for history)

Clean Architecture (layered, dependency rule: Domain ← Application ← Infrastructure, Api wires all) with Vertical Slice as the work-breakdown lens, not a folder-per-feature layer-collapse. `Application`/`Domain` were organized into four named module sub-namespaces plus a cross-cutting one, instead of a flat `Application/Ports` bag:

| Module | Owns | PRD sections |
|---|---|---|
| `Compliance` | SubAccount, EntityRegistration lifecycle | §3, §4 |
| `Ledger` | Wallet, FundAccount, DepositAddress, Transaction, BalanceSnapshot, transfers, redemptions, balances | §6, §7, §8, §9 |
| `Webhooks` | Durable inbox, dedup, per-topic processors, notification outbox | §10, §10.1 |
| `Admin` | Cross-tenant/master-account read views | §2.5 |
| `Shared` | Cross-cutting auth (`ICallerContext`, `TenantScopeResolver`), cross-module provider ports, shared config | n/a |

Full modular-monolith (independent persistence/deployment per module) was **not** adopted.

**Why it was reversed:** in practice, three organizing axes (layer / module /
use-case) proved confusing to traverse, and a brief attempt to extend the module
axis into Domain and Infrastructure for consistency (2026-07-19) made the tension
worse rather than better — Infrastructure genuinely doesn't divide cleanly by
business module (a shared `DbContext`, shared Circle client options), and EF
migrations can't be modularized at all. That attempt was reverted in favor of
dropping the module axis everywhere instead of forcing it everywhere.

Original rationale (for the historical record): Domain had real aggregates
(SubAccount+EntityRegistration state machine, Wallet+Transaction+BalanceSnapshot
invariants) and a mandated `Money` value object, so plain thin-domain VSA was
rejected in favor of naming bounded contexts as sub-namespaces. Full
modular-monolith isolation was rejected because the PRD scopes a single deployable,
single region, single tenant model, single provider (§1.3, §14).
