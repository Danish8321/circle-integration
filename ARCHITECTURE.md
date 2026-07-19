# Architecture — how to read this repo

**One sentence:** this is **Clean Architecture**. Handlers are filed by *module*,
then by *use-case*. That folder convention is the only thing on top of Clean — there
is no separate "VSA framework" to learn.

If a request feels hard to follow, it is because a request crosses all four tiers.
This document traces one from HTTP to database so you see a whole flow once.

---

## The one rule: dependencies point inward

```
        ┌───────────────────────────────────────────────┐
        │  Api        (controllers, middleware, DI wiring)│  ── wires everything
        └───────────────────────────────────────────────┘
                          │ references
                          ▼
        ┌───────────────────────────────────────────────┐
        │  Infrastructure  (DbContext, EF, Circle gateway)│
        └───────────────────────────────────────────────┘
                          │ implements ports from
                          ▼
        ┌───────────────────────────────────────────────┐
        │  Application  (handlers, ports = interfaces)    │
        └───────────────────────────────────────────────┘
                          │ references
                          ▼
        ┌───────────────────────────────────────────────┐
        │  Domain     (entities, Money, invariants)       │  ── references nothing
        └───────────────────────────────────────────────┘
```

- **Domain** knows nothing about anyone. No EF, no ASP.NET, no `DateTime.Now`.
- **Application** defines **ports** (interfaces like `ISubAccountGateway`,
  `ISubAccountRepository`) and use-case **handlers**. It does not know SQL Server or
  `DbContext` exist.
- **Infrastructure** *implements* those ports (real Circle HTTP gateway, EF
  repositories). Nobody inward of it references it.
- **Api** is thin: resolve tenant → build a command → call a handler → return a DTO.
  It also wires everything in `Program.cs`.

Arrows only point inward. That is the whole architecture.

---

## The three ways things are grouped (so nothing feels like magic)

1. **Layer** (Clean) — Domain / Application / Infrastructure / Api. *Which project.*
2. **Module** — `Compliance`, `Ledger`, `Webhooks`, `Admin`, `Shared`. *Which business area.*
3. **Use-case folder** — `CreateSubAccount/`, `GetSubAccount/`. *Which single operation.*

So a handler lives at `Application / Compliance / CreateSubAccount /`
= layer / module / use-case. Once you know the operation, the path writes itself.

Older docs call axis 3 "VSA / Vertical Slice". Ignore the buzzword: it just means
"one folder per use-case." Layers stay strict and horizontal — slices do **not** cut
through or collapse them.

---

## Where does X live?

| I want to change / add… | Go to |
|---|---|
| A new HTTP endpoint | `Api/<Module>/<Thing>Controller.cs` (thin), + a request/response DTO beside it |
| Request-shape validation | `Api/<Module>/<UseCase>RequestValidator.cs` (FluentValidation; a global filter runs it) |
| A new use-case (business operation) | `Application/<Module>/<UseCase>/` — a `Command`/`Query`, a `Handler`, a `Result` |
| A new port (something the handler needs from outside) | `Application/<Module>/Ports/I<Name>.cs` (interface only) |
| The real implementation of a port | `Infrastructure/` (`Persistence/…Repository.cs` or `Providers/Circle/…Gateway.cs`) |
| A new entity / invariant / state transition | `Domain/<Entity>.cs` (private setters, static `Create`, guarded transitions) |
| Wire a port → implementation | `Program.cs` (one file; see gateway env-gating below) |
| A schema change | `.claude/scripts/schema.sh new` → **read** the migration → `apply` |

---

## One request, traced end to end: `POST /v1/sub-accounts`

Follow the clickable refs; every hop is real.

**1. Api — thin controller**
- `src/TreasuryServiceOrchestrator.Api/Compliance/SubAccountsController.cs:77` — the
  `[HttpPost]` action. Resolves the tenant from `ICallerContext` (never trusts the
  raw body id): `TenantScopeResolver.Resolve(...)` at `:85`, builds the command `:90`,
  dispatches to the handler, returns `CreatedAtAction` with a `CreateSubAccountResponse` `:106`.
- Validation is **global**, not per-action: `Program.cs:38` registers
  `ValidationActionFilter`, which resolves `IValidator<T>` for each action arg and
  returns an RFC 7807 `ProblemDetails` 400 on failure.

**2. Application — the handler (the orchestration)**
- `src/TreasuryServiceOrchestrator.Application/Compliance/CreateSubAccount/CreateSubAccountHandler.cs:24`
  — `HandleAsync`. Shape:
  - admin-only guard → `TenantForbiddenException`
  - command-level validation
  - `IdempotencyExecutor.ExecuteAsync(...)` wraps the money-safe sequence:
    - **reserve**: `SubAccount.Create(...)` → `repo.AddAsync` → audit `"SubAccountRequested"` → `SaveChangesAsync`
    - **gateway**: `ISubAccountGateway.CreateExternalEntityAsync(...)`
    - **complete**: `SubAccount.BeginCompliance(walletId)` → `EntityRegistration.Create(...)` → commit
  - Ports it depends on: `Compliance/Ports/ISubAccountGateway.cs`, `ISubAccountRepository.cs`,
    `IEntityRegistrationRepository.cs`.

**3. Domain — the rules**
- `src/TreasuryServiceOrchestrator.Domain/SubAccount.cs` — `Create` (state `Created`),
  `BeginCompliance` (`Created → PendingCompliance`, sets `CircleWalletId`).
- `src/TreasuryServiceOrchestrator.Domain/EntityRegistration.cs` — `Create` (`Pending`).
  Private setters, static factories, guarded transitions. Zero framework refs.

**4. Infrastructure — the real outside world**
- `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs`
  — implements `ISubAccountGateway`, typed `HttpClient`, `POST v1/externalEntities`.
- `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/SubAccountRepository.cs`
  — implements `ISubAccountRepository` over `DbContext` (use-case-shaped queries, no
  generic `IRepository<T>`). `UnitOfWork.cs` commits.

**5. DI wiring — `Program.cs` (one file)**
- Handlers / repos / validators: around `Program.cs:54-67`.
- Gateway port → implementation is **environment-gated** (`Program.cs:166-194`):
  - mock mode → `MockSubAccountGateway` (hard-blocked in Production by `MockModeGuard.Validate`)
  - Development → `FakeSubAccountGateway`
  - Production → real `CircleSubAccountGateway` with a resilient HTTP handler.

Read that chain once and every other use-case (`GetSubAccount`, `ListSubAccounts`, the
`Ledger` operations) is the same shape in a different module folder.

---

## Pointers

- The accepted decision behind the module grouping: `docs/adr/0001-module-boundaries.md`.
- Tier rules and invariants (money type, idempotency, tenant isolation): `.claude/CLAUDE.md`.
- Ubiquitous language / glossary: `CONTEXT.md`.
