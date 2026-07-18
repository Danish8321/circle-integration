Status: all four items resolved (2 shipped, 2 doc-only pending real DB provisioning) — closed 2026-07-18

Source: `docs/features/06-audit-and-compliance.md` §3, §5.3, §5.4, §7; `docs/README.md` §7.
Blocked by: none (cross-cutting — touches Api middleware/response shape, Infrastructure
persistence config, and future logging work).

## Scope

Four separate, independently-actionable gaps bundled under one ticket since none is large enough
to be its own feature slice:

1. **Correlation id not echoed to the caller.** `TraceIdentifier` is forwarded into every
   `auditLog.AppendAsync(...)` call server-side, but no `X-Correlation-Id`-style response header
   (or RFC 7807 `traceId` extension member) exists anywhere in `src/` today. PRD §14 ("structured
   logging with correlation ids on every request/event") implies the caller should be able to
   reference it (e.g. filing a support ticket) — currently it's server-log-only.
2. **Audit immutability is accidental, not DB-enforced.** No append-only constraint, trigger, or
   equivalent exists at the persistence layer — nothing currently stops an `UPDATE`/`DELETE`
   against the audit table beyond "no code path does it."
3. **No retention implementation/ops record.** PRD §14's 7-year audit retention requirement has
   no corresponding backup/archive/purge policy documented or built.
4. **PII-at-rest encryption not independently verified.** PRD §12 requires entity-registration
   details and bank data encrypted at rest — a SQL Server TDE-or-equivalent concern, not
   application code. Not checked against actual Infrastructure-tier database provisioning because
   that provisioning isn't documented yet.

Explicitly **not** in scope here: secrets-in-logs audit (`06` §5.4) — deferred until a structured
logging implementation actually exists to audit; today's audit `PayloadJson` blobs don't carry
secrets (API keys/bank credentials are gateway-layer, not command fields), so there's nothing to
check yet. Re-open as a fifth item here if/when a command DTO gains a sensitive field.

## Decisions resolved during grilling (2026-07-17)

1. **Correlation id: `X-Correlation-Id` response header, on every response, not just RFC 7807
   error bodies.** A middleware (or the existing exception-handling middleware, extended) sets it
   from the same `TraceIdentifier`/`CorrelationId` already forwarded into `auditLog.AppendAsync`
   calls — success responses need it too (a caller filing a support ticket about a 200 that later
   turned out wrong still needs to reference the request), which a header gives and an
   error-only RFC 7807 extension member would not.
2. **Audit immutability, retention, PII-at-rest**: revisited 2026-07-18 — see
   `docs/adr/0008-audit-persistence-target.md`. Item 2 (DB-enforced immutability) is **not**
   actually blocked: `test-full.sh` already runs against a real SQL Server instance via
   Testcontainers, so a migration-level trigger/constraint can be built and verified now. Items 3-4
   (retention runbook, TDE plan) stay doc-only for now, scoped against the ADR's self-hosted
   SQL Server target; actually enabling TDE / standing up backup jobs still waits on a real
   non-ephemeral instance.

## Definition of done

- [x] Correlation id echoed on every response — `X-Correlation-Id` header, decision recorded above.
- [x] Audit-table immutability enforced at the DB layer — migration
      `20260718053127_AddAuditRecordImmutabilityTrigger` (`INSTEAD OF UPDATE, DELETE` trigger on
      `AuditRecords`), proved by `AuditRecordImmutabilityTests` (2 cases: UPDATE and DELETE both
      throw `SqlException`, row survives), `test-full.sh` 56/56 green. Also fixed a real test-infra
      bug along the way: `TreasuryServiceOrchestratorApiFactory` used `EnsureCreatedAsync()`
      (model-snapshot schema), which silently skipped every migration's raw SQL including this
      trigger — switched to `MigrateAsync()` so integration tests exercise real migrations.
- [x] Retention/ops record exists — `docs/ops/audit-retention.md`, doc-only, scoped against
      self-hosted SQL Server (ADR 0008); re-open to execute once a real instance exists.
- [x] TDE plan documented — `docs/ops/pii-at-rest-tde-plan.md`, doc-only, scoped against
      self-hosted SQL Server (ADR 0008); actual enablement still blocked on a real non-ephemeral
      instance and a secrets manager for the DMK password.

## Comments
