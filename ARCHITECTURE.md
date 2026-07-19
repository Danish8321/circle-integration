# Architecture — how to read this repo

**One sentence:** this is plain **Clean Architecture**. Nothing else. Every tier is
organized by *kind of thing* (controller, handler, port, DTO, validator, entity,
repository, gateway) — no business-module folders, no per-use-case folders.

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

- **Domain** knows nothing about anyone. No EF, no ASP.NET, no `DateTime.Now`. Flat —
  one file per entity/value object/enum, no subfolders.
- **Application** defines **ports** (interfaces) and use-case **handlers**. It does
  not know SQL Server or `DbContext` exist.
- **Infrastructure** *implements* those ports (real Circle HTTP gateway, EF
  repositories). Nobody inward of it references it.
- **Api** is thin: resolve tenant → build a command → call a handler → return a DTO.
  It also wires everything in `Program.cs`.

Arrows only point inward. That is the whole architecture.

---

## Folders, by tier — grouped by kind, not by business area

| Tier | Folders | What goes there |
|---|---|---|
| **Domain** | *(flat, no subfolders)* | One file per entity, value object (`Money`), enum. |
| **Application** | `Handlers/` `Ports/` `Dtos/` `Validators/` `Services/` `Exceptions/` | `Handlers/` = every `*Handler.cs`, plus `ICommandHandler`/`IQueryHandler` (the one exception to "interfaces live in `Ports/`" — they're generic dispatch contracts, not something Infrastructure implements). `Ports/` = every port interface (`ISubAccountGateway`, `IUnitOfWork`, `ICallerContext`, …) and the request/result shapes that cross a port. `Dtos/` = every `Command`/`Query`/`Result`. `Validators/` = FluentValidation classes. `Services/` = cross-cutting app logic that isn't a handler (`IdempotencyExecutor`, `TenantScopeResolver`, `LedgerPostingService`, status mappers, background-service option classes). `Exceptions/` = the exception taxonomy. |
| **Infrastructure** | `Persistence/` `Providers/Circle/` `Mocks/` `Webhooks/` `Notifications/` `Reconciliation/` `Snapshots/` `Migrations/` | Grouped by technical concern: EF repositories + `DbContext`, real Circle HTTP gateways, mock/fake gateways, webhook inbox/processors, notification dispatch, background reconciliation/snapshot services, EF migrations (flat — one per `DbContext`, an EF Core constraint). |
| **Api** | `Controllers/` `Dtos/` `Validators/` `Middleware/` | `Controllers/` = every `*Controller.cs` (thin). `Dtos/` = every request/response record. `Validators/` = FluentValidation for requests. `Middleware/` = pipeline pieces (tenant resolution, correlation id, exception handling). `Program.cs` stays at the root — it's the one file that wires everything. |

No file's home depends on "which business area" — only on "what kind of thing is
this." A request/result DTO for `Ledger` and one for `Compliance` sit in the same
`Dtos/` folder; the filename (`CreateSubAccountCommand.cs`, `CreateTransferCommand.cs`)
is what tells them apart.

---

## Where does X live?

| I want to change / add… | Go to |
|---|---|
| A new HTTP endpoint | `Api/Controllers/<Thing>Controller.cs` (thin), + a request/response record in `Api/Dtos/` |
| Request-shape validation | `Api/Validators/<UseCase>RequestValidator.cs` (FluentValidation; a global filter runs it) |
| A new use-case (business operation) | `Application/Dtos/<UseCase>Command.cs` (or `Query`), `Application/Handlers/<UseCase>Handler.cs`, `Application/Dtos/<UseCase>Result.cs` |
| A new port (something the handler needs from outside) | `Application/Ports/I<Name>.cs` |
| The real implementation of a port | `Infrastructure/Persistence/…Repository.cs` or `Infrastructure/Providers/Circle/…Gateway.cs` |
| A new entity / invariant / state transition | `Domain/<Entity>.cs` (private setters, static `Create`, guarded transitions) |
| Wire a port → implementation | `Api/DependencyInjection/*ServiceCollectionExtensions.cs` (see below) |
| A schema change | `.claude/scripts/schema.sh new` → **read** the migration → `apply` |

---

## One request, traced end to end: `POST /v1/sub-accounts`

Follow the clickable refs; every hop is real.

**1. Api — thin controller**
- `src/TreasuryServiceOrchestrator.Api/Controllers/SubAccountsController.cs:77` — the
  `[HttpPost]` action. Resolves the tenant from `ICallerContext` (never trusts the
  raw body id): `TenantScopeResolver.Resolve(...)` at `:85`, builds the command `:90`,
  dispatches to the handler, returns `CreatedAtAction` with a `CreateSubAccountResponse` `:106`.
- Validation is **global**, not per-action: `Program.cs:38` registers
  `ValidationActionFilter`, which resolves `IValidator<T>` for each action arg and
  returns an RFC 7807 `ProblemDetails` 400 on failure.

**2. Application — the handler (the orchestration)**
- `src/TreasuryServiceOrchestrator.Application/Handlers/CreateSubAccountHandler.cs:24`
  — `HandleAsync`. Shape:
  - admin-only guard → `TenantForbiddenException`
  - command-level validation
  - `IdempotencyExecutor.ExecuteAsync(...)` (`Application/Services/`) wraps the
    money-safe sequence:
    - **reserve**: `SubAccount.Create(...)` → `repo.AddAsync` → audit `"SubAccountRequested"` → `SaveChangesAsync`
    - **gateway**: `ISubAccountGateway.CreateExternalEntityAsync(...)`
    - **complete**: `SubAccount.BeginCompliance(walletId)` → `EntityRegistration.Create(...)` → commit
  - Ports it depends on: `Application/Ports/ISubAccountGateway.cs`, `ISubAccountRepository.cs`,
    `IEntityRegistrationRepository.cs`. Command/result shapes:
    `Application/Dtos/CreateSubAccountCommand.cs`, `CreateSubAccountResult.cs`.

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

**5. DI wiring — `Api/DependencyInjection/*.cs`, called from `Program.cs`**
- `Program.cs` is ~30 lines: `builder.AddWebApiCore()`, `.AddInfrastructurePersistence()`,
  `.AddApplicationHandlers()`, `.AddCircleIntegration()`, `.AddBackgroundServices()`, then the
  middleware pipeline.
- Handlers / validators: `Api/DependencyInjection/ApplicationServiceCollectionExtensions.cs`.
- Repos / `DbContext`: `Api/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`.
- Gateway port → implementation is **environment-gated**,
  `Api/DependencyInjection/CircleIntegrationServiceCollectionExtensions.cs`:
  - mock mode → `MockSubAccountGateway` (hard-blocked in Production by `MockModeGuard.Validate`)
  - Development → `FakeSubAccountGateway`
  - Production → real `CircleSubAccountGateway` with a resilient HTTP handler.

Read that chain once and every other use-case (`GetSubAccount`, `ListSubAccounts`, the
`Ledger` operations) is the same shape — same folders, different filenames.

---

## Pointers

- The accepted decision behind this layout: `docs/adr/0001-module-boundaries.md`.
- Tier rules and invariants (money type, idempotency, tenant isolation): `.claude/CLAUDE.md`.
- Ubiquitous language / glossary: `CONTEXT.md`.
