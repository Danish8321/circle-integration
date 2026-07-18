# 0008 — Self-hosted SQL Server as eventual deployment target; Testcontainers unblocks DB-enforced audit work now

Status: accepted, 2026-07-18

## Context

Ticket 13 (`.scratch/treasury-service-orchestrator/issues/13-audit-compliance-gaps.md`) items 2-4
were marked blocked on "real SQL Server provisioning outside LocalDB dev." Two things needed
resolving:

1. Whether that blocker still holds, given `test-full.sh` already runs the Api tier against a real
   SQL Server instance via Testcontainers (per CLAUDE.md's testing-strategy table) — not LocalDB.
2. What deployment target retention/TDE decisions (items 3-4) should assume, since none was
   documented anywhere in `docs/`.

## Decision

- **Deployment target: self-hosted SQL Server** (Docker/VM, not Azure SQL). Not provisioned yet,
  but this is the platform retention runbooks and TDE cert-management docs should be written
  against — self-hosted means manual TDE certificate/key management and manual backup/retention
  jobs, not a managed service's built-in equivalents.
- **Item 2 (DB-enforced audit immutability) is not actually blocked.** Testcontainers already gives
  `test-full.sh` a real SQL Server instance with real trigger/constraint semantics. Build the
  `INSTEAD OF UPDATE, DELETE` trigger (or equivalent constraint) in a migration now, verify it
  against Testcontainers, same as any other schema change in this repo.
- **Items 3-4 stay design/doc-only for now** — a retention runbook and TDE plan can be written
  against the self-hosted-SQL-Server assumption, but actually enabling TDE and standing up backup
  jobs waits until a real instance exists outside test infra (Testcontainers containers are
  ephemeral, not a backup/retention target).

## Consequences

- Ticket 13 item 2 moves from "blocked" to actionable — scoped as its own sub-task.
- Items 3-4 produce docs (runbook, TDE plan) now, self-hosted-SQL-Server-specific, but no
  infrastructure stood up yet; re-open to *execute* those docs once a real instance is provisioned.
- Future provisioning work should default to self-hosted SQL Server, not propose Azure SQL, unless
  this ADR is revisited.
