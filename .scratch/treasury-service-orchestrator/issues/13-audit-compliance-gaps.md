Status: open

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
2. **Audit immutability, retention, PII-at-rest**: still open — genuinely blocked on
   Infrastructure-tier database provisioning that doesn't exist yet (no DB-trigger/constraint
   code, no backup/archive tooling, no TDE config anywhere in `src/` to verify against). Deferred
   to whichever ticket first stands up real SQL Server provisioning outside LocalDB dev — not
   resolvable by a docs-only pass since there is no implementation to point a decision at yet.

## Definition of done

- [x] Correlation id echoed on every response — `X-Correlation-Id` header, decision recorded above.
- [ ] Audit-table immutability enforced at the DB layer (or explicit decision recorded that
      code-level-only is accepted risk, with sign-off) — blocked on real DB provisioning.
- [ ] Retention/ops record exists (even a doc-only runbook) for the 7-year requirement — blocked
      on real DB provisioning.
- [ ] PII-at-rest encryption confirmed against actual DB provisioning once Infrastructure's
      database setup is documented — blocked on real DB provisioning.

## Comments
