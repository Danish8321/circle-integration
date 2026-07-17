# Audit & Compliance

Covers immutable audit records (who/what/when/before-after/correlation id/provider refs), 7-year
retention, correlation-id propagation, Travel Rule compliance posture, and PII/secrets handling
(managed secret store, no secrets in config/logs).

Source: `docs/PRD.md` §12 (Cross-cutting: Audit & Compliance), §7.3 (Travel Rule correction),
§14 (Non-Functional Requirements — security/observability rows); `docs/Phase_3_Circle_Integration_Plan.md`
Task 8 (Secrets) and Task 9 (mTLS decision); `docs/Phase_1_Feature_Slices.md` (usage sites of
`IAuditLogService`, no formal type definition — shipped code is ground truth, see §2 below).

Tenancy/authorization terminology (`ClientCompanyId`, caller identity vs. target scope, Admin
audit-on-all-tenant-access) is canonical in `01-tenancy-and-authorization.md` — this file does
not restate it, only the audit-record mechanics and cross-cutting compliance/secrets posture.
RFC 7807 error-contract mapping is `05-reliability-and-error-handling.md`'s concern, not this
file's.

---

## 1. Scope / PRD requirement

Source: PRD §12.

- **Immutable audit record** for every state-changing action: who (`ClientCompanyId` + caller
  identity), what (operation + resource), when, before/after state, correlation id, provider
  reference ids. Audit records are append-only — cannot be modified or deleted through any API.
- **Retention**: 7 years (financial-records standard).
- **Correlation ids** flow from consumer request through provider calls, webhook processing, and
  audit records.
- **Travel Rule**: satisfied structurally via account-on-file identity + recipient verification,
  not a per-call request field (§4 below — re-verified live this pass).
- **PII**: entity registration details and bank data are encrypted at rest; secrets (provider API
  keys) live in a managed secret store with rotation support, never in configuration files (§5
  below).

---

## 2. `AuditRecord` entity + `IAuditLogService` port design

`Phase_1_Feature_Slices.md` never defines `AuditRecord`/`IAuditLogService` formally — every task
from Task 1 onward just consumes `IAuditLogService auditLog` as a constructor dependency and
calls `auditLog.AppendAsync(...)` at the "reserve" and "complete" points of the two-`SaveChanges`
pattern (invariant 11). The shape below is the **shipped code**, which is the reference for this
file.

### 2.1 `AuditRecord` (Domain)

`src/TreasuryServiceOrchestrator.Domain/AuditRecord.cs`

```csharp
public class AuditRecord
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public string ClientCompanyId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    private AuditRecord() { }

    public static AuditRecord Create(
        string eventType, string entityType, string entityId, string payloadJson,
        string clientCompanyId, string correlationId, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(), EventType = eventType, EntityType = entityType, EntityId = entityId,
        PayloadJson = payloadJson, ClientCompanyId = clientCompanyId, CorrelationId = correlationId,
        OccurredAtUtc = nowUtc,
    };
}
```

Private setters and a private constructor plus the `Create` factory are the entity's only
mutation path — there is no `Update`/setter surface, which is the structural half of "audit
records cannot be modified" (domain invariant, satisfies `docs/agents` Domain-tier rule: no
`DateTime.Now`, `nowUtc` is caller-supplied via `TimeProvider`).

**PRD wording vs. shipped shape — reconciled, not a defect.** PRD §12 says "before/after state";
the shipped entity has a single `PayloadJson` blob, not separate `Before`/`After` columns. In
practice every call site (`CreateSubAccountHandler`, `ResubmitEntityRegistrationHandler`,
`SetSubAccountDisabledHandler`, `ListSubAccountsHandler`) serializes whatever state changed —
either the inbound command or a projection of the fields that changed — into `PayloadJson` via
`JsonSerializer.Serialize(...)`. This satisfies the PRD's intent (the changed state is captured
and traceable) without a rigid before/after column pair, which would not generalize across very
different resource shapes (a sub-account state transition vs. a balance mutation vs. a list
access). This file documents the shipped shape as correct; a stricter before/after schema is not
warranted unless a future compliance reviewer specifically requires field-level diffs.

### 2.2 `IAuditLogService` port (Application)

`src/TreasuryServiceOrchestrator.Application/Shared/Abstractions/IAuditLogService.cs`

```csharp
public interface IAuditLogService
{
    Task AppendAsync(
        string eventType,
        string entityType,
        string entityId,
        string payloadJson,
        string clientCompanyId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
```

`AppendAsync` has no return value — callers don't need the generated `Id` (no handler currently
returns an audit-record reference to its caller), and it deliberately does not accept a
`DateTime`/`TimeProvider` parameter — the timestamp is the implementation's responsibility, not
the caller's, keeping every call site free of a `TimeProvider` dependency it would otherwise need
only for this one call.

### 2.3 `AuditLogService` (Infrastructure)

`src/TreasuryServiceOrchestrator.Infrastructure/Persistence/AuditLogService.cs`

```csharp
public sealed class AuditLogService(TreasuryServiceOrchestratorDbContext dbContext, TimeProvider timeProvider)
    : IAuditLogService
{
    public async Task AppendAsync(
        string eventType, string entityType, string entityId, string payloadJson,
        string clientCompanyId, string correlationId, CancellationToken cancellationToken = default)
    {
        var record = AuditRecord.Create(
            eventType, entityType, entityId, payloadJson, clientCompanyId, correlationId,
            timeProvider.GetUtcNow().UtcDateTime);

        await dbContext.AuditRecords.AddAsync(record, cancellationToken);
    }
}
```

`AppendAsync` stages the record on the tracked `DbContext` but does **not** call
`SaveChangesAsync` itself — every call site is inside a handler that owns its own
`unitOfWork.SaveChangesAsync(cancellationToken)` boundary (invariant 11's two-`SaveChanges`
pattern: reserve → gateway/state-transition → complete). This means an audit record is only
durable when the surrounding handler's transaction commits — if the handler fails before its
`SaveChangesAsync`, the staged audit record rolls back with everything else, which is correct
(no orphaned audit entry for an action that never actually happened).

Registered in `Program.cs`: `builder.Services.AddScoped<IAuditLogService, AuditLogService>();`.

### 2.4 EF mapping — immutability gap, flagged

`src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`:

```csharp
modelBuilder.Entity<AuditRecord>(entity =>
{
    entity.HasKey(x => x.Id);
    entity.Property(x => x.EventType).IsRequired().HasMaxLength(100);
    entity.Property(x => x.ClientCompanyId).IsRequired().HasMaxLength(64);
});
```

**Open gap, not yet closed.** "Audit records are append-only and cannot be modified or deleted
through any API" is true today only because **no API surface exposes `AuditRecord` at all** — no
`AuditController`, no update/delete port method exists anywhere in the codebase. That is an
accident of scope, not a structural guarantee: nothing in the EF configuration, the database
schema, or `IAuditLogService`'s port shape (which has only `AppendAsync`, no `UpdateAsync`) stops
a future engineer from adding a mutating method to a hypothetical `IAuditLogService` extension or
writing directly against `DbSet<AuditRecord>` from a new handler. Recommended follow-up (not
implemented yet): a database-level guard (e.g. a `DENY UPDATE, DELETE` grant on the audit table
for the application's SQL login, or an `INSTEAD OF UPDATE/DELETE` trigger that raises) so
immutability holds even against a bug, not just against the current absence of a mutating code
path. Tracked as an open item in §7.

No index exists yet on `(ClientCompanyId, EntityType, EntityId)` or `(OccurredAtUtc)` — acceptable
for now since no query/list endpoint reads `AuditRecord` back out; add one when an audit-retrieval
endpoint (Admin-only, per PRD §2.4's audited all-tenant-access pattern) is built.

### 2.5 Retention — not yet implemented

Nothing in the shipped code enforces or automates the PRD §12 / §14 "7 years, immutable" retention
requirement — there is no purge job (correctly, since purge is not wanted before 7 years) and no
documented backup/archive policy tying the audit table to the DR requirements in PRD §14
(RPO 15 min / RTO 4 hr apply to the whole database, audit table included, but nothing calls that
out specifically for the 7-year horizon). Tracked as an open item in §7 — likely resolved by
ops/backup policy rather than application code, but worth an explicit decision record before
Phase 3 sign-off.

---

## 3. Correlation-id propagation

Source of truth: `HttpContext.TraceIdentifier`, read once at the API boundary and threaded through
every command/query as an explicit field — never re-derived mid-handler, never taken from a
header the caller controls.

```csharp
// src/TreasuryServiceOrchestrator.Api/Compliance/SubAccountsController.cs
var correlationId = HttpContext.TraceIdentifier;
```

Every mutating and audited-read command/query DTO carries a `string CorrelationId` field (e.g.
`CreateSubAccountCommand`, `ResubmitEntityRegistrationCommand`, `SetSubAccountDisabledCommand`,
`ListSubAccountsQuery`), validated `NotEmpty().MaximumLength(128)` by the matching FluentValidation
validator (`CreateSubAccountValidator`, `ResubmitEntityRegistrationValidator`) — satisfying the
"every endpoint has a validation filter" invariant for this field specifically, not just payload
fields. Handlers forward `command.CorrelationId` unchanged into every `auditLog.AppendAsync(...)`
call, so a single `TraceIdentifier` links every audit record produced by one request, and (per
PRD §11's provider-call design) the same value is available to attach to outbound Circle calls
and inbound webhook processing for that logical operation.

**Resolved 2026-07-17 grilling (ticket 13): `X-Correlation-Id` response header, echoed on every
response, not just RFC 7807 error bodies.** Chosen over an error-only RFC 7807 `traceId` extension
member because success responses need it too — a caller filing a support ticket about a 200 that
later turned out wrong still needs a way to reference the request, which an error-only extension
member would not give them. Set from the same `TraceIdentifier`/`CorrelationId` already forwarded
into every `auditLog.AppendAsync` call, so the header value matches the correlation id on the
audit records the request produced.

---

## 4. Travel Rule compliance posture (verified live this pass)

**Structural, not per-call-field.** This product calls `POST /v1/businessAccount/transfers` for
outbound transfers. That endpoint's request body carries **no originator name/address fields** —
`source` is `{ id, type: "wallet" }` only. Compliance is satisfied structurally: the Distributor's
identity already on file with Circle, plus the recipient-verification step (Mint Console human
approval on recipient registration, PRD §7.1), is what Circle's Travel Rule engine consumes for
this flow. CLAUDE.md invariant 12 codifies this: outbound transfer commands in this codebase must
never carry Travel Rule originator name/address fields.

This is distinct from `POST /v1/payouts` (the crypto/stablecoin payout endpoint), which **does**
carry Travel Rule fields — `source.identities[]` (originator identity: `type`, `name`,
`addresses[]`) and, for `CIRCLE_SG`/`CIRCLE_FR`-booked payouts, `purposeOfTransfer` and Address
Book `identity`/`ownership` objects. This product does not call `POST /v1/payouts` — it is not
part of this product's endpoint surface (Appendix B).

**Live re-verification, this pass (2026-07-17):**

- **Local mirror** (`docs/circle-mint-docs/reference/travel-rule-compliance.md`) — confirmed live
  against `https://developers.circle.com/circle-mint/references/travel-rule-compliance` on
  2026-07-07 per its own header note; the mirror only documents `source.identities[]` and
  `purposeOfTransfer` as fields of `POST /v1/payouts`, and separately notes originator data is
  only *readable* off `businessAccount/transfers` via `GET .../{id}?returnIdentities=true` — a
  response-retrieval mechanism for regulated institutions, not a request field this product would
  ever populate.
- **Live WebFetch**, this pass, against
  `https://developers.circle.com/circle-mint/references/travel-rule-compliance` — result: "only
  `POST /v1/payouts` carries Travel Rule originator identity and payment reason code fields in its
  request body"; `POST /v1/businessAccount/transfers` does not include `source.identities[]` or
  `purposeOfTransfer`. **Confirms the posture above — no discrepancy found.**

No product code in this repo constructs a Travel Rule originator-identity payload; the transfer
command/gateway request types (Ledger module, `POST /v1/businessAccount/transfers` callers) carry
only `source`/`destination`/`amount`/idempotency fields, consistent with this posture.

---

## 5. Secrets & PII handling

### 5.1 Managed secret store — decision, partially implemented

PRD §12/§14: provider API keys live in a managed secret store with rotation support, never in
configuration files; no secrets in config or logs.

`docs/Phase_3_Circle_Integration_Plan.md` Task 8 scopes this precisely:

- Sandbox and production Circle API keys are stored as **separate secrets**
  (`Circle:ApiKey:Sandbox`, `Circle:ApiKey:Production` or equivalent), resolved by environment at
  startup — never both loaded into the same process.
- A startup health check must fail fast (not silently fall back to mock mode) if the expected
  secret is missing in a non-mock environment — this is the enforcement mechanism tying secrets
  handling to invariant 9 (mock mode structurally impossible in Production): a missing secret
  must be a hard startup failure, not a silent mock-mode fallback.
- Task 8's rotation-cadence bullet ("scheduled reminder at day 150 of a 180-day key lifetime") was
  **dropped** — it only applied if mTLS were adopted (see §5.2), and Task 9 decided against mTLS.
  Circle API keys under the current (no-mTLS) posture have no enforced rotation cadence; "rotation
  support" means the secret store mechanism supports rotation on demand, not that rotation is
  scheduled/automatic.
- **Status: not yet implemented in shipped code.** No `SecretClient`/Key Vault/managed-secret-store
  reference exists in `src/` today (checked — no hits for `SecretClient`, `KeyVault`, or a Circle
  API key configuration path at all, since no Circle gateway HTTP client has been wired yet in the
  currently-shipped Compliance-module-only code). This is expected at this stage of the build
  (Circle gateway plumbing is a Phase 3 task), not a doc-drift defect — flagged in §7 so it isn't
  lost.

### 5.2 mTLS — decided posture, live-reverified

**Decision (already made, not re-litigated here): skip mTLS.** Per
`docs/Phase_3_Circle_Integration_Plan.md` Task 9 (decided 2026-07-17, doc-grilling): this is a
US-only entity with no MiCA exposure. mTLS for Circle Mint is optional for non-MiCA entities;
enabling it would revoke all existing API keys immediately (a deliberate-cutover event, not a
toggle) and impose a mandatory 180-day key lifetime for a compliance requirement this org does
not have. Task 9 is a no-op; Task 8's rotation-reminder bullet correctly drops out with it (§5.1).
Revisit only if the org gains EU/EEA MiCA-regulated status.

**Live re-verification, this pass (2026-07-17):**

- Local mirror (`docs/circle-mint-docs/README.md`) — as of 2026-07-17, the mirror's mTLS
  setup/rotation howto pages were removed as unconfirmable; the one surviving live-sourced
  sentence (from `getting-started-with-the-circle-apis`, confirmed 2026-07-07) reads: mTLS is
  "available for additional security and may be required for entities under EU MiCA regulation."
- **Live WebFetch**, this pass, against
  `https://developers.circle.com/circle-mint/getting-started-with-the-circle-apis` — result:
  "Opting in is optional for most customers and required for entities operating under the EU
  Markets in Crypto-Assets (MiCA) regulation." **Confirms the mirror and the decision's underlying
  premise — mTLS remains optional for this (US-only, non-MiCA) entity. No discrepancy found; the
  skip-mTLS decision still holds.**

### 5.3 PII at rest

PRD §12: entity registration details and bank data are encrypted at rest. This is a database/
infrastructure-level control (SQL Server transparent data encryption or equivalent), not an
application-code concern this file's shipped code touches — **not independently verified against
shipped infrastructure config as part of this pass**; flagged in §7 as an item to confirm once
the Infrastructure tier's database provisioning is documented (out of scope for this
greenfield-Compliance-module snapshot).

### 5.4 No secrets in logs

No structured-logging implementation exists yet in shipped code to audit for secret-leakage risk
(PRD §14's "structured logging with correlation ids" is not yet built). When it is, the same
`PayloadJson`/command-serialization pattern used for audit records (`JsonSerializer.Serialize(command)`
in `CreateSubAccountHandler` etc.) is worth a specific check at that time — command DTOs in this
codebase do not currently carry secrets (API keys, bank credentials are gateway-layer concerns,
not command fields), so today's audit `PayloadJson` blobs are not a leakage vector, but this
should be re-checked whenever a command DTO gains a sensitive field.

---

## 6. Tests required

Per the testing strategy in `.claude/CLAUDE.md`:

**Domain** — `AuditRecord.Create` factory: rejects nothing today (no validation inside `Create`),
so the only meaningful domain-level test is that `Create` populates all fields from its arguments
and generates a fresh `Id`/uses the supplied `nowUtc` rather than any ambient clock (guards against
a future regression reintroducing `DateTime.UtcNow` inside the entity).

**Application** (xUnit v3, Moq/NSubstitute, FluentAssertions) — every handler that calls
`auditLog.AppendAsync(...)` already substitutes `IAuditLogService` in its test doubles
(`Substitute.For<IAuditLogService>()` appears across `CreateSubAccountHandlerTests`,
`ResubmitEntityRegistrationHandlerTests`, `SetSubAccountDisabledHandlerTests`,
`ListSubAccountsHandlerTests`); at minimum one test per handler should assert
`auditLog.Received(1).AppendAsync(expectedEventType, expectedEntityType, ..., correlationId, ...)`
with the exact event-type string, not just "was called" — event-type string drift (e.g.
`"SubAccountRequested"` vs `"SubAccountCreated"`) is exactly the kind of doc/code drift this
restructure exists to catch, and a loose `Received(1).AppendAsync(Arg.Any<...>())` assertion would
not catch it.

`ListSubAccountsHandler`'s all-tenant-access audit (`"SubAccountsListed"`) needs its own test
distinct from the tenant-forbidden test in `01-tenancy-and-authorization.md` §3 — specifically
that a successful `AllTenants` list call audits *before* or alongside the unfiltered query, not
that access is merely permitted.

**Api** (WebApplicationFactory + Testcontainers, real SQL Server) — an integration test asserting
an `AuditRecord` row actually lands in the database (via `TreasuryServiceOrchestratorDbContext`)
after a real end-to-end mutating call (e.g. `POST` sub-account creation), with `CorrelationId`
matching the response's/request's `TraceIdentifier` — this is the one test that would catch a
regression where a handler's `SaveChangesAsync` boundary silently drops the staged audit record
(§2.3's "audit record is only durable when the surrounding transaction commits" behavior needs a
real-DB test, not a mocked one, to prove).

No dedicated retention/purge test exists or is needed yet (§2.5 — no purge job implemented).

---

## 7. Open corrections / decisions log

**PRD §12 "before/after state" vs. shipped single-`PayloadJson`-blob shape — reconciled, not a
defect.** See §2.1. This file documents the shipped shape as the correct reading; no code change
recommended unless a compliance reviewer specifically requires field-level before/after diffing.

**Audit-record immutability is accidental (no API surface), not structural — open gap.** See §2.4.
No database-level guard (deny grant / `INSTEAD OF` trigger) or code-level enforcement stops a
future mutating code path from being added to `AuditRecord`/`IAuditLogService`. Recommend closing
before any `AuditController` or admin audit-retrieval endpoint ships, since that is exactly the
point at which "structurally impossible to modify" needs to become true rather than incidental.

**7-year retention: not implemented, no decision record yet.** See §2.5. No purge/archival job,
no explicit backup-policy cross-reference to the 7-year horizon. Needs an explicit ops decision
(likely SQL Server backup/retention policy, not application code) before Phase 3 sign-off.

**Correlation id echo — resolved 2026-07-17.** See §3: `X-Correlation-Id` response header, set on
every response from `HttpContext.TraceIdentifier` (ticket 13).

**Portal human-user audit header (PRD §2.2) — resolved 2026-07-17 grilling (ticket 14):
explicitly deferred, not a gap.** No portal/client exists in this repo (API-only, no Angular/TS
client per CLAUDE.md) and no portal authentication mechanism exists to source a human identity
from — designing a header shape now would be speculative with no consumer to validate it against.
Re-open when a portal auth client is actually built. PRD §2.2 requires the human portal user
driving an Admin action to be passed in a separate header and
recorded for audit only. No such header exists in `CallerIdentityMiddleware` or any
`AuditRecord`/`IAuditLogService` call site today (`ClientCompanyId` is the only identity captured).
Genuine gap, not a doc-drift correction — open until a feature slice implements it.

**Managed secret store: decided design, not yet implemented.** See §5.1. `Phase_3_Circle_Integration_Plan.md`
Task 8 fully specifies the design; no `SecretClient`/Key Vault/managed-secret-store code exists in
`src/` yet, consistent with Circle gateway plumbing being unbuilt at this stage. Not a defect —
tracked so this file's next revision (once Task 8 ships) updates §5.1 from "decided, not
implemented" to "implemented, verified."

**Travel Rule posture — re-verified live 2026-07-17, no discrepancy found.** See §4. Both the
local mirror and a fresh live `WebFetch` against
`https://developers.circle.com/circle-mint/references/travel-rule-compliance` agree:
`POST /v1/businessAccount/transfers` carries no originator identity fields; only
`POST /v1/payouts` does. CLAUDE.md invariant 12 stands unchanged.

**mTLS posture — re-verified live 2026-07-17, no discrepancy found.** See §5.2. Fresh live
`WebFetch` against `https://developers.circle.com/circle-mint/getting-started-with-the-circle-apis`
confirms: "Opting in is optional for most customers and required for entities operating under the
EU Markets in Crypto-Assets (MiCA) regulation." Matches the local mirror's surviving citation and
the Task 9 skip-mTLS decision's premise. No change to the decision.

**PII-at-rest encryption — not independently verified this pass.** See §5.3. Out of scope for a
Compliance-module-only shipped-code snapshot; flag for the Infrastructure/DB-provisioning
documentation once it exists.
