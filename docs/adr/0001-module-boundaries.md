# ADR 0001: Clean Architecture, handlers organized by module and use-case

**Status:** Accepted (2026-07-16)

> **Plain-language summary.** This is **Clean Architecture** — the only hard rule is
> the dependency direction: Domain ← Application ← Infrastructure, Api wires all.
> Inside the Application (and Api) layer, handlers are grouped first by **module**
> (`Compliance`, `Ledger`, `Webhooks`, `Admin`, `Shared`), then by **use-case**
> (`CreateSubAccount/`, `GetSubAccount/`, …). That grouping is a filing convention,
> nothing more. Where the docs say "Vertical Slice / VSA", read it as
> "use-case-named folders as the work-breakdown lens" — **not** feature slices that
> cut through or collapse the layers. Layers stay strict and horizontal.
> See `ARCHITECTURE.md` at the repo root for a traversal map and one fully-traced request.

## Decision

Clean Architecture (layered, dependency rule: Domain ← Application ← Infrastructure, Api wires all), with handlers organized by module then use-case. Use-case-named folders are the work-breakdown lens — a filing convention, not a folder-per-feature layer-collapse. `Application`/`Domain` are organized into four named module sub-namespaces plus a cross-cutting one, instead of a flat `Application/Ports` bag:

| Module | Owns | PRD sections |
|---|---|---|
| `Compliance` | SubAccount, EntityRegistration lifecycle | §3, §4 |
| `Ledger` | Wallet, FundAccount, DepositAddress, Transaction, BalanceSnapshot, transfers, redemptions, balances | §6, §7, §8, §9 |
| `Webhooks` | Durable inbox, dedup, per-topic processors, notification outbox | §10, §10.1 |
| `Admin` | Cross-tenant/master-account read views | §2.5 |
| `Shared` | Cross-cutting auth (`ICallerContext`, `TenantScopeResolver`), cross-module provider ports, shared config | n/a |

Full modular-monolith (independent persistence/deployment per module) was **not** adopted.

## Rationale

Domain has real aggregates (SubAccount+EntityRegistration state machine, Wallet+Transaction+BalanceSnapshot invariants) and a mandated `Money` value object, so plain thin-domain VSA was rejected. Four bounded contexts are already visible in the PRD's own section boundaries (§3-4, §6-9, §10, §2.5); naming them as sub-namespaces now is cheap, retrofitting after handlers accumulate in a flat folder is expensive.

Full modular-monolith isolation was rejected: the PRD scopes a single deployable, single region, single tenant model, single provider (§1.3, §14) — no isolation pressure to justify independent persistence/deployment per module.

## Consequences

New ports/handlers go under their module sub-namespace (`Application/<Module>/Ports`, `Application/<Module>/<UseCase>`), not a flat `Application/Ports`. Any implementation plan assuming a flat structure must be reconciled before scaffolding.
