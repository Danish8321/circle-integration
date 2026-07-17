# Sub-Account & Entity Registration

Covers the `SubAccount`/`EntityRegistration` domain split, the `Created → PendingCompliance →
Active|Rejected` lifecycle plus the `Disabled` overlay, resubmission after rejection, and the
sub-account CRUD-equivalent endpoints (create/get/list/disable-enable/resubmit).

Terminology (`ClientCompanyId`, tenant identity, Admin vs SubAccount role, caller identity vs
target scope) is canonical in `01-tenancy-and-authorization.md` — this file does not restate it,
only uses it. `TenantScopeResolver` usage here follows that file's controller pattern (§2.5)
exactly. RFC 7807 error mapping (`tenant-forbidden`, `not-found`, `conflict`, `validation`) is
owned by `05-reliability-and-error-handling.md`. Mock-mode gateway swapping is owned by
`02-mock-mode.md`. Module ownership: `Compliance` (Application/Domain) per
`docs/adr/0001-module-boundaries.md`.

---

## 1. Scope / PRD requirement

Source: `docs/PRD.md` §3 (Domain Model & Entity Lifecycle), §4 (Capability: Sub-Account & Entity
Management), Appendix A items 1–2, Appendix B relevant rows.

- **SubAccount** is the product's tenant, keyed 1:1 by `ClientCompanyId`. Created by the admin;
  maps 1:1 to its *active* `EntityRegistration`.
- **EntityRegistration** is one submission of institutional-client business/address details to
  Circle for compliance screening. A sub-account has exactly one active registration and zero or
  more rejected historical ones.
- Circle's actual External Entity lifecycle is minimal and provider-driven: creation triggers
  synchronous sanctions screening (`complianceState: PENDING`), and the final `ACCEPTED`/
  `REJECTED` decision arrives asynchronously on the `externalEntities` webhook topic. Entities
  **cannot be edited or deleted** — a `REJECTED` entity is permanently unusable and the only
  remedy is submitting a **new** entity with corrected details (PRD §3.2).
- The product models this as its own lifecycle:

  ```mermaid
  stateDiagram-v2
      [*] --> Created : admin creates sub-account
      Created --> PendingCompliance : submit entity details
      PendingCompliance --> Active : webhook ACCEPTED (wallet usable)
      PendingCompliance --> Rejected : webhook REJECTED
      Rejected --> PendingCompliance : resubmit corrected details (new registration)
      Active --> Disabled : admin disables (internal only)
      Disabled --> Active : admin re-enables
  ```

- **Wallet gating** — `walletId` is returned at creation but unusable while the registration is
  `PENDING` or `REJECTED`. No wallet-scoped operation is accepted until the sub-account is
  `Active` (PRD §3.2 rule 2).
- **Disabled is an internal-only overlay** — Circle has no suspend/close concept for entities;
  `Disabled` is enforced entirely by this API (blocks new money-moving operations, reads remain
  available) and is orthogonal to compliance state (PRD §3.2 rule 4).
- **One active registration per sub-account** at any time; rejected registrations are retained as
  history, never deleted (PRD §3.2 rule 5).
- Operations table (PRD §4.1):

  | Operation | Access | Notes |
  |---|---|---|
  | Create sub-account | Admin | Binds to the target `ClientCompanyId` (1:1, immutable); submits entity details in the same call. |
  | Submit / resubmit entity registration | Admin, owning SubAccount | Resubmission allowed only from `Rejected`. |
  | Get sub-account | Admin, owning SubAccount | Lifecycle state, active registration status, wallet id, rejection reason. |
  | List sub-accounts | Admin | Filterable by lifecycle state. |
  | Disable / enable sub-account | Admin | Internal overlay state only. |

---

## 2. Domain design

`src/TreasuryServiceOrchestrator.Domain/SubAccount.cs`, `EntityRegistration.cs`,
`SubAccountLifecycleState.cs`, `EntityRegistrationStatus.cs`.

Both entities are mutable classes with private setters and a private constructor — state changes
only happen through named methods that enforce the state machine, not through property setters
from outside the Domain tier.

```csharp
public enum SubAccountLifecycleState { Created, PendingCompliance, Active, Rejected }

public class SubAccount
{
    public Guid Id { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public SubAccountLifecycleState LifecycleState { get; private set; }
    public bool IsDisabled { get; private set; }
    public string? CircleWalletId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SubAccount Create(string clientCompanyId, DateTime nowUtc);   // -> Created
    public void BeginCompliance(string circleWalletId);                        // Created -> PendingCompliance
    public void MarkRejected();                                                // PendingCompliance -> Rejected
    public void ResubmitCompliance();                                          // Rejected -> PendingCompliance
    public void SetDisabled(bool disabled);                                    // overlay, any state
}
```

```csharp
public enum EntityRegistrationStatus { Pending, Accepted, Rejected }

public class EntityRegistration
{
    public Guid Id { get; private set; }
    public Guid SubAccountId { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public string BusinessName { get; private set; } = string.Empty;
    public string BusinessUniqueIdentifier { get; private set; } = string.Empty;
    public string IdentifierIssuingCountryCode { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Postcode { get; private set; } = string.Empty;
    public string StreetName { get; private set; } = string.Empty;
    public string BuildingNumber { get; private set; } = string.Empty;
    public string? CircleWalletId { get; private set; }
    public EntityRegistrationStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static EntityRegistration Create(/* subAccountId, ClientCompanyId, business/address fields, circleWalletId, nowUtc */);  // -> Pending
    public void Reject(string reason, DateTime nowUtc);   // Pending -> Rejected
}
```

Notably: **there is no `Accept`/`MarkActive` method on either entity in the shipped code** — see
§8 (open corrections) for what this means for the lifecycle diagram's `PendingCompliance -->
Active` transition.

`CircleWalletId` naming follows `docs/adr/0005-provider-agnostic-naming.md`'s stated policy of
"rename when the next task touches it, not as a standalone sweep" — both entities still use the
Circle-prefixed name pervasively; this is the ADR's documented gradual-migration state, not a
defect to fix in this slice.

---

## 3. Application design

Module: `Application.Compliance` (`src/TreasuryServiceOrchestrator.Application/Compliance/`),
one folder per use case, per `.claude/CLAUDE.md`'s VSA convention.

### 3.1 Ports (`Compliance/Ports/`)

```csharp
public interface ISubAccountGateway
{
    Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken = default);
}

public interface ISubAccountRepository
{
    Task AddAsync(SubAccount subAccount, CancellationToken cancellationToken = default);
    Task<SubAccount?> GetByClientCompanyIdAsync(string clientCompanyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubAccount>> ListAsync(
        SubAccountLifecycleState? lifecycleState = null, CancellationToken cancellationToken = default);
}

public interface IEntityRegistrationRepository
{
    Task AddAsync(EntityRegistration entityRegistration, CancellationToken cancellationToken = default);
    Task<EntityRegistration?> GetLatestForSubAccountAsync(
        Guid subAccountId, CancellationToken cancellationToken = default);
}
```

`ISubAccountGateway` owns **only** `CreateExternalEntityAsync` in the shipped code — see §8 for
where this diverges from the plan docs and `02-mock-mode.md`. There is no
`GetByCircleWalletIdAsync` on `ISubAccountRepository` and no `GetByCircleWalletIdAsync`/
`GetByCircleWalletIdAsync` lookup path anywhere in Compliance today (also §8 — no consumer of it
exists yet).

The gateway request/result DTOs live in `Application/Shared/Ports/` (cross-module — the same
shape Ledger's real gateway calls will eventually reuse for other Circle wallet-scoped ops):

```csharp
public sealed record CreateExternalEntityGatewayRequest(
    string BusinessName, string BusinessUniqueIdentifier, string IdentifierIssuingCountryCode,
    string Country, string State, string City, string Postcode, string StreetName, string BuildingNumber);

public sealed record CreateExternalEntityResult(
    string WalletId, string ComplianceState, string BusinessName, string BusinessUniqueIdentifier);
```

### 3.2 Use cases

| Use case | Command/Query | Handler | Result |
|---|---|---|---|
| Create sub-account | `CreateSubAccount/CreateSubAccountCommand` | `CreateSubAccountHandler` | `CreateSubAccountResult(SubAccountId, ClientCompanyId, CircleWalletId, SubAccountLifecycleState LifecycleState)` |
| Get sub-account | `GetSubAccount/GetSubAccountQuery` | `GetSubAccountHandler` | `SubAccountDetailsResult(SubAccountId, ClientCompanyId, string LifecycleState, IsDisabled, CircleWalletId, string? LatestRegistrationStatus, string? RegistrationRejectionReason)` |
| List sub-accounts | `ListSubAccounts/ListSubAccountsQuery` | `ListSubAccountsHandler` | `IReadOnlyList<SubAccountDetailsResult>` |
| Disable/enable sub-account | `SetSubAccountDisabled/SetSubAccountDisabledCommand` | `SetSubAccountDisabledHandler` | `SetSubAccountDisabledResult(SubAccountId, ClientCompanyId, IsDisabled)` |
| Resubmit entity registration | `ResubmitEntityRegistration/ResubmitEntityRegistrationCommand` | `ResubmitEntityRegistrationHandler` | `ResubmitEntityRegistrationResult(SubAccountId, ClientCompanyId, RegistrationId, string LifecycleState, string RegistrationStatus)` |

`SubAccountDetailsResult` and both lifecycle-carrying results use `string` for
`SubAccountLifecycleState`/`EntityRegistrationStatus` (via `.ToString()`, produced by
`SubAccountDetailsMapper.Map`), not the enum type — `CreateSubAccountResult` is the one exception,
still carrying the enum directly. This is a real, if minor, inconsistency in the shipped code —
flagged, not fixed, in §8.

**`CreateSubAccountHandler`** (reserve → gateway → complete, per CLAUDE.md invariant 11):

1. Admin-only guard (`callerContext.IsAdmin`, else `TenantForbiddenException`) — creation always
   targets an explicit tenant an Admin names; a SubAccount caller has no sub-account of its own to
   create one for.
2. Validate (`CreateSubAccountValidator`, FluentValidation).
3. `IdempotencyExecutor.ExecuteAsync` keyed on `(ClientCompanyId, IdempotencyKey)`.
4. **Reserve**: `SubAccount.Create` (state `Created`) persisted, audited (`SubAccountRequested`),
   first `SaveChangesAsync`.
5. **Gateway/state-transition**: `gateway.CreateExternalEntityAsync(...)`, then
   `subAccount.BeginCompliance(gatewayResult.WalletId)` (→ `PendingCompliance`),
   `EntityRegistration.Create(...)` (status `Pending`, mapped via `EntityRegistrationStatusMapper`
   from `gatewayResult.ComplianceState`).
6. **Complete**: registration persisted, audited (`SubAccountProvisionedAtCircle`), second
   `SaveChangesAsync` (implicit via the idempotency executor's unit-of-work wrap).

Note the handler never branches on `registrationStatus == Accepted`/`Rejected` to short-circuit
straight to `Active`/`Rejected` — every successful create lands in `PendingCompliance`, because
Circle's `CreateExternalEntityAsync` response's `complianceState` is always `PENDING` synchronously
(§4, live-verified) — see §8 for how this differs from the historical plan doc, which described a
(never-observable) synchronous accept/reject branch.

**`ResubmitEntityRegistrationHandler`** — no admin-only guard; a SubAccount caller may resubmit
its own rejected registration, an Admin may resubmit for a named tenant (tenant scoping handled by
the controller's `TenantScopeResolver`, per `01-tenancy-and-authorization.md` §2.5):

1. Validate, then `IdempotencyExecutor.ExecuteAsync`.
2. Look up the sub-account by `ClientCompanyId`; `NotFoundException` if missing.
3. Guard: `subAccount.LifecycleState != Rejected` → `ConflictException` ("only a Rejected
   registration may be resubmitted").
4. **Reserve**: `subAccount.ResubmitCompliance()` (→ `PendingCompliance`), audited
   (`EntityRegistrationResubmitted`), `SaveChangesAsync`.
5. **Gateway/state-transition**: `gateway.CreateExternalEntityAsync(...)` with the corrected
   details, a fresh `EntityRegistration.Create(...)` (new `Id`, same `SubAccountId`) — the prior
   rejected registration row is untouched, retained as history (PRD §3.2 rule 3/5).
6. **Complete**: new registration persisted, audited (`EntityRegistrationResubmitCompleted`).

**`ListSubAccountsHandler`** — Admin/`AllTenants`-only, defense-in-depth re-check of
`callerContext.IsAdmin` independent of the resolved `TenantScope` (mirrors the pattern
`01-tenancy-and-authorization.md` §2.6 documents), audits `SubAccountsListed` before the unfiltered
`ISubAccountRepository.ListAsync` query.

**`SetSubAccountDisabledHandler`** — Admin-only (defense-in-depth re-check, same pattern), calls
`subAccount.SetDisabled(command.Disabled)`, audits `SubAccountDisabledSet`.

**`GetSubAccountHandler`** — no role guard beyond the controller's tenant scoping (an owning
SubAccount caller or Admin naming that tenant may read); looks up the sub-account, then its latest
registration, maps via `SubAccountDetailsMapper.Map`.

### 3.3 Api tier

`src/TreasuryServiceOrchestrator.Api/Compliance/SubAccountsController.cs`, route
`v1/sub-accounts`:

| Endpoint | Route | Access |
|---|---|---|
| Create | `POST v1/sub-accounts` | Admin only (`request.ClientCompanyId` is the **target** tenant; resolved via `TenantScopeResolver`, cast to `SingleTenant`) |
| Get | `GET v1/sub-accounts/{clientCompanyId}` | Admin, owning SubAccount |
| List | `GET v1/sub-accounts?state=...` | Admin only |
| Resubmit | `POST v1/sub-accounts/{clientCompanyId}/registrations` | Admin, owning SubAccount |
| Disable/enable | `PUT v1/sub-accounts/{clientCompanyId}/disabled` | Admin only |

Request/response DTOs live alongside the controller
(`src/TreasuryServiceOrchestrator.Api/Compliance/*.cs`): `CreateSubAccountRequest`/
`CreateSubAccountResponse`, `SubAccountResponse` (shared by Get and List), `SetSubAccountDisabledRequest`/
`SetSubAccountDisabledResponse`, `ResubmitEntityRegistrationRequest`/`ResubmitEntityRegistrationResponse`
— each with a paired `*RequestValidator` (`CreateSubAccountRequestValidator`,
`ResubmitEntityRegistrationRequestValidator`), satisfying CLAUDE.md invariant 6 (every endpoint
has a validation filter). `Create` and `Resubmit` return `CreatedAtAction` (`201`); `Get`/`List`/
`SetDisabled` return `Ok` (`200`) — matching the actual controller, not a REST-dogma assumption.

Every handler method follows `01-tenancy-and-authorization.md` §2.5's pattern exactly: resolve via
`TenantScopeResolver.Resolve(callerContext, ...)`, cast to `SingleTenant` for route-scoped
endpoints, pass the resolved `TenantScope` straight through for `List`. The controller never reads
`ClientCompanyId` from the route or body directly as the tenant (Api tier rule).

---

## 4. Circle provider mapping (live-verified)

| Product operation | Circle endpoint | Verified |
|---|---|---|
| Submit / resubmit entity registration | `POST /v1/externalEntities` | Live, this pass — see §7 |
| Get entity (fallback poll) | `GET /v1/externalEntities/{walletId}` | Live, this pass |
| List entities | `GET /v1/externalEntities` | Live, this pass |
| Compliance decision | `externalEntities` webhook topic | Locally-mirrored page, itself dated live-verified 2026-07-07/07-16/07-17 — see §7 |

Request body: `businessName`, `businessUniqueIdentifier`, `identifierIssuingCountryCode`,
`address: { country, state, city, postalCode, streetName?, buildingNumber? }`. **No
`idempotencyKey` field exists on this endpoint** (confirmed live) — Circle's own dedup for this
call is the `409` on a duplicate `businessUniqueIdentifier` + country pair; local idempotency
still runs through the standard two-`SaveChangesAsync` pattern (CLAUDE.md invariant 11) keyed on
the caller-supplied `Idempotency-Key` header, which only protects against local double-submission,
not a genuinely different registration for the same business.

Response (both create and get): `walletId`, `businessName`, `businessUniqueIdentifier`,
`identifierIssuingCountryCode`, `complianceState` ∈ `{PENDING, ACCEPTED, REJECTED}` — confirmed
live via the OpenAPI spec.

`EntityRegistrationStatusMapper.Map` does a case-insensitive uppercase match of these three
literals to `EntityRegistrationStatus`, throwing `ArgumentOutOfRangeException` on anything else —
there is no "unknown, treat as pending" fallback here (contrast with the recipient-status mapper
in PRD §7.1, which does map unknown literals to pending-equivalent; entity compliance states are a
closed, small set Circle has never changed, so a hard failure on an unrecognized value is the
correct choice for this mapper specifically).

---

## 5. Mock-mode behavior

See `02-mock-mode.md` for the full mock-mode design (deterministic screening, simulated webhooks,
failure/latency injection, production guard). For this slice specifically: the shipped
Development-only stand-in is `FakeSubAccountGateway`
(`src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/FakeSubAccountGateway.cs`), not
the `MockSubAccountGateway` name `02-mock-mode.md` and the Phase 1 plan describe — see §8. It
always returns `ComplianceState: "PENDING"` with a synthesized `dev-{guid}` wallet id; it has no
magic-suffix rejection behavior and does not emit any webhook, since — per §8 below — nothing in
this module consumes an `ACCEPTED`/`REJECTED` decision yet regardless of mock or real mode.

---

## 6. Real Circle HTTP integration

`src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs`
(per `docs/Phase_3_Circle_Integration_Plan.md` Task 2), constructor-injected `HttpClient` (named
`"Circle"` client from Task 1, `IHttpClientFactory`-backed per CLAUDE.md invariant 3):

```csharp
public sealed class CircleSubAccountGateway(HttpClient httpClient) : ISubAccountGateway
{
    public async Task<CreateExternalEntityResult> CreateExternalEntityAsync(
        CreateExternalEntityGatewayRequest request, CancellationToken cancellationToken = default)
    {
        var circleRequest = new CreateExternalEntityCircleRequest { /* maps 1:1 from request */ };
        using var response = await httpClient.PostAsJsonAsync("v1/externalEntities", circleRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ExternalEntityCircleEnvelope>(cancellationToken)
            ?? throw new InvalidOperationException("Circle returned an empty externalEntities response.");
        return new CreateExternalEntityResult(
            envelope.Data.WalletId, envelope.Data.ComplianceState, envelope.Data.BusinessName,
            envelope.Data.BusinessUniqueIdentifier);
    }
}
```

Supporting wire-shape types (`Infrastructure/Providers/Circle/`): `CreateExternalEntityCircleRequest`,
`CircleExternalEntityAddress`, `ExternalEntityCircleEnvelope` (the `{ "data": {...} }` envelope
Circle wraps every response in), `ExternalEntityCircleData`.

Two gaps against `Phase_3_Circle_Integration_Plan.md` Task 2, both flagged in §8, not fixed here:

1. **`response.EnsureSuccessStatusCode()` throws the raw `HttpRequestException`** on any non-2xx —
   Task 2 requires every HTTP error to route through `CircleErrorTranslator` (Task 7) before the
   gateway throws, so a caller sees a PRD §11.2 error class (`provider-rejected`,
   `provider-unavailable`, ...), never a raw transport exception. `CircleErrorTranslator` does not
   exist in the shipped code yet (confirmed by grep — Task 7 is not started).
2. **`GetExternalEntityAsync` (the `GET /v1/externalEntities/{walletId}` fallback-poll method) is
   not implemented anywhere** — not on `ISubAccountGateway`, not on `CircleSubAccountGateway`, not
   on `FakeSubAccountGateway`. PRD §4.2's reconciliation-fallback failure path ("the decision
   webhook never arrives → reconciliation fallback polls `GET /v1/externalEntities/{walletId}`")
   has no implementation surface today.

Per the Circle-fact re-verification in §4/§7, `CreateExternalEntityAsync`'s Circle call carries no
idempotency field, so there is nothing to forward/audit here matching CLAUDE.md invariant 11's
"idempotency key forwarded to the provider on money-moving calls" — entity creation is not itself
a money-moving call, and `Phase_3_Circle_Integration_Plan.md` Task 4's idempotency-forwarding audit
explicitly excludes this gateway's method from its list (`CreateTransferAsync`, `RedeemAsync`,
`GenerateDepositAddressAsync`, `RegisterRecipientAsync`, `CreateLinkedBankAccountAsync` — all on
`CircleMintGateway`/`IStablecoinGateway`, none on `ISubAccountGateway`).

---

## 7. Live Circle-fact verification (this pass)

| Claim | Result | Source |
|---|---|---|
| `POST /v1/externalEntities`, `GET /v1/externalEntities/{walletId}`, `GET /v1/externalEntities` shapes | **Confirmed live.** Request: `businessName`, `businessUniqueIdentifier`, `identifierIssuingCountryCode`, `address {country, state, city, postalCode, streetName?, buildingNumber?}`. Response: `walletId`, `businessName`, `businessUniqueIdentifier`, `identifierIssuingCountryCode`, `complianceState`. | `https://developers.circle.com/openapi/institutional.yaml`, fetched live this pass |
| No `idempotencyKey` field on `POST /v1/externalEntities` | **Confirmed live.** | Same OpenAPI fetch |
| `complianceState` enum is exactly `{PENDING, ACCEPTED, REJECTED}` | **Confirmed live.** | Same OpenAPI fetch |
| Lifecycle is provider-driven: synchronous `PENDING` on create, async `ACCEPTED`/`REJECTED` via `externalEntities` webhook topic; entities cannot be edited/deleted, only resubmitted as new | **Not re-fetched live this pass** — the narrative docs pages (`/circle-mint/docs/external-entities`, `/circle-mint/docs/institutional-onboarding-external-entities`) both 404'd on live fetch this pass. Falling back to the local mirror `docs/circle-mint-docs/howtos/manage-institutional-subaccounts.md`, whose own header states it was verified live 2026-07-07 with corrections re-verified 2026-07-16/07-17 — content matches this claim exactly (Step 1/2, "Endpoint reference" table). | Local mirror, itself live-dated |
| `walletId` returned at creation but unusable while `PENDING`/`REJECTED` | **Not re-fetched live this pass**, same 404 as above. Confirmed via the same local-mirror page's explicit callout: "The `walletId` is returned at creation but remains unusable while the entity is in `PENDING` or `REJECTED`. Wait for the compliance decision before referencing the entity's wallet in any other endpoint." | Local mirror, itself live-dated |

No discrepancies found against the PRD's existing claims (PRD's own header records a full
26-claim live re-verification pass 2026-07-17, 0 discrepancies — this file's narrower pass is
consistent with that). The discrepancies actually found this pass are all **doc-vs-shipped-code**
drift, not doc-vs-Circle drift — see §8.

---

## 8. Tests required

Per the testing-strategy table in `.claude/CLAUDE.md`.

**Domain** (xUnit v3) — entity invariants and state-machine transitions:
- `SubAccount.Create` rejects blank `ClientCompanyId`; starts in `Created`, not disabled.
- `SubAccount.BeginCompliance` only from `Created` (else `InvalidOperationException`); rejects
  blank `circleWalletId`; result state `PendingCompliance`.
- `SubAccount.MarkRejected` only from `PendingCompliance`.
- `SubAccount.ResubmitCompliance` only from `Rejected`.
- `SubAccount.SetDisabled` is unconstrained by lifecycle state (works from any state — it's an
  independent overlay).
- `EntityRegistration.Create` rejects empty `subAccountId`/blank `clientCompanyId`/blank
  `businessName`; starts `Pending`.
- `EntityRegistration.Reject` only from `Pending`; rejects blank reason.

**Application** (xUnit v3, NSubstitute — the shipped tests use NSubstitute, not Moq as
`.claude/CLAUDE.md`'s generic testing-strategy table states; treat NSubstitute as the actual
convention for this module) — handler orchestration against mocked ports:
- `CreateSubAccountHandler`: non-Admin caller → `TenantForbiddenException`; duplicate
  `ClientCompanyId` → `SubAccountAlreadyExistsException`; success persists `SubAccount` (state
  `PendingCompliance`, `CircleWalletId` from gateway) and an `EntityRegistration` (status mapped
  from gateway `ComplianceState`); idempotent replay short-circuits via `IIdempotencyService`.
- `ResubmitEntityRegistrationHandler`: sub-account not `Rejected` → `ConflictException`; success
  transitions `PendingCompliance`, adds a second `EntityRegistration` row, retains the first.
- `GetSubAccountHandler`: missing sub-account → `NotFoundException`; maps latest registration's
  status/rejection reason through.
- `ListSubAccountsHandler`: non-Admin or non-`AllTenants` scope → `TenantForbiddenException`
  even when the (hypothetical, buggy) resolver handed it `AllTenants`; success audits then lists.
- `SetSubAccountDisabledHandler`: non-Admin → `TenantForbiddenException`; missing sub-account →
  `NotFoundException`; toggles `IsDisabled` independent of `LifecycleState`.

**Api** (WebApplicationFactory + Testcontainers, real SQL Server) — full pipeline, currently
covered by `tests/TreasuryServiceOrchestrator.IntegrationTests/Compliance/`:
- `SubAccountsControllerTests.cs`: Admin + idempotency key → `201` with `PendingCompliance`; no
  `ClientCompanyId` header → `401`; non-Admin caller (including for their own tenant) → `403`
  `tenant-forbidden`; blank `BusinessName` → `400`; duplicate create for the same tenant → `409`.
- `GetSubAccountTests.cs`: owning SubAccount caller → `200` with full details; cross-tenant caller
  → `403`; Admin naming an existing tenant → `200`; Admin naming a nonexistent tenant → `404`.
- `ResubmitEntityRegistrationTests.cs`: owning caller resubmitting a `Rejected` sub-account → `201`,
  new registration row added (asserted via a direct `DbContext` count — `2` registrations total),
  lifecycle back to `PendingCompliance`; resubmit when not `Rejected` → `409`, registration count
  unchanged at `1`; cross-tenant caller → `403`; Admin naming a nonexistent tenant → `404`.

Every integration test in this set constructs its fixtures by driving the real HTTP pipeline (an
Admin-authenticated `POST v1/sub-accounts` call, or a direct `TreasuryServiceOrchestratorDbContext`
seed via `SubAccount.Create`/`subAccount.MarkRejected()`/`registration.Reject(...)`) rather than
hand-building entities with reflection — matching the Api tier's "real DB round trip" testing goal.

---

## 9. Open corrections / decisions log

**`ISubAccountGateway` is narrower than the plan/mock-mode docs describe — resolved by using
shipped code.** The task brief, `02-mock-mode.md` §2, and `Phase_3_Circle_Integration_Plan.md`
Task 2's provider-mapping table all describe `ISubAccountGateway` as owning both
`CreateExternalEntityAsync` **and** `GetExternalEntityAsync`. The actual shipped interface
(`src/TreasuryServiceOrchestrator.Application/Compliance/Ports/ISubAccountGateway.cs`) has only
`CreateExternalEntityAsync`. **Resolved: this file documents the one method that actually exists.**
`GetExternalEntityAsync` (the `GET /v1/externalEntities/{walletId}` fallback-poll method PRD §4.2
requires for the "webhook never arrives" failure path) is unimplemented — a genuine gap, not a
naming drift, flagged for whichever task builds PRD §11.4's reconciliation fallback.

**Fake gateway is named `FakeSubAccountGateway`, not `MockSubAccountGateway`.**
`02-mock-mode.md` §2's table and the Phase 1 plan both name the Development stand-in
`MockSubAccountGateway`. The shipped type
(`src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/FakeSubAccountGateway.cs`) is
named `FakeSubAccountGateway` and its own doc comment explicitly distinguishes itself from "the
formal mock-provider system (Phase 1 Task 6, PRD §13) which this repo doesn't have yet." **Resolved:
this file uses the shipped name and treats it as a narrower, temporary stand-in** — it has none of
`02-mock-mode.md`'s deterministic-rejection, simulated-webhook, or failure-injection behavior; it
always synthesizes a `dev-{guid}` wallet id and `PENDING` state. Whoever implements Phase 1 Task 6's
real mock-mode system will replace this file's `FakeSubAccountGateway` with the described
`MockSubAccountGateway`, not extend it in place.

**No code path transitions a `SubAccount`/`EntityRegistration` to `Active`/`Accepted` yet — a real
gap, not a doc-drift correction.** `SubAccount` has `BeginCompliance`, `MarkRejected`,
`ResubmitCompliance`, and `SetDisabled`, but no `MarkAccepted`/equivalent; `EntityRegistration` has
`Reject` but no `Accept`. Grepping the whole `src/` tree for `SubAccountLifecycleState.Active`,
`MarkAccepted`, and `.Accept(` (compliance-state acceptance, not the unrelated `HttpClient`
`Accept` header) returns zero matches, and there is no `externalEntities` webhook processor
(`ProcessExternalEntityDecisionHandler`, named in `Phase_1_Feature_Slices.md` Task 3, does not
exist in `src/`). **Left open, not resolved** — the `PendingCompliance --> Active` edge in this
file's §1 lifecycle diagram (and PRD §3.2's) describes the intended design, verified against
Circle's own webhook shape (§7), but has no implementation today. This belongs to whichever task
builds the webhook inbox → dedup → per-topic-processor pipeline for the `externalEntities` topic
(`Phase_1_Feature_Slices.md` Task 5, referenced but out of scope for `03-webhook-processing.md`'s
current content — confirm before assuming that file already covers it).

**Lifecycle/status fields are `string` in Application DTOs, not the Domain enum, with one
inconsistent exception.** `Phase_1_Feature_Slices.md`'s Task 4 snippets show
`SubAccountDetailsResult`/`SetSubAccountDisabledResult`/`ResubmitEntityRegistrationResult` carrying
`SubAccountLifecycleState`/`EntityRegistrationStatus` as enum-typed fields. The shipped
`SubAccountDetailsResult`, `SetSubAccountDisabledResult` (drops the lifecycle field entirely), and
`ResubmitEntityRegistrationResult` all use `string` (via `SubAccountDetailsMapper.Map`'s
`.ToString()` calls), while `CreateSubAccountResult` alone still carries the enum. **Resolved: this
file documents the shipped shapes as-is** (§3.2) and flags the enum/string split as a minor,
pre-existing inconsistency — not something this doc-authoring pass should silently paper over by
picking one and pretending it's uniform, but also not something to fix here (out of scope: no code
changes in this task).

**Route paths and field names: Phase_1 Task 4's snippets vs. shipped code diverge; shipped code
kept.** Task 4's controller snippet uses `POST v1/sub-accounts` with a `TargetClientCompanyId` body
field and a `POST /{clientCompanyId}/resubmit` route. Shipped code
(`SubAccountsController.cs`) uses `ClientCompanyId` (not `TargetClientCompanyId`) as the body field
name on `CreateSubAccountRequest`, and `POST /{clientCompanyId}/registrations` (not `/resubmit`) for
resubmission. **Resolved: this file's §3.3 table documents the shipped routes/names** — same
resolution pattern as `01-tenancy-and-authorization.md` §4's caller-registry correction (plan
snippets are historical design notes, not a contract; shipped code wins).

**Class names not owned by `IQueryHandler`/`ICommandHandler` generic contracts everywhere.**
`GetSubAccountHandler` and `ListSubAccountsHandler` are plain classes with a `HandleAsync` method
and no interface implementation in the shipped code (unlike `CreateSubAccountHandler`,
`ResubmitEntityRegistrationHandler`, and `SetSubAccountDisabledHandler`, which do implement
`ICommandHandler<TCommand, TResult>`). Documented as-is in §3.2's table; not a defect this file
resolves, since the controller injects concrete handler types either way (Task 4's own note: "the
controller injects concrete handler types, matching the DI style").
