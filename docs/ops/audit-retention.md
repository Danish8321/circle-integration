# Audit retention runbook

Status: doc-only, not yet executed — no non-ephemeral SQL Server instance exists to run this
against (see `docs/adr/0008-audit-persistence-target.md`). Written now so the policy is agreed
before there's infrastructure to enforce it.

Source requirement: PRD §14 — 7-year audit retention.

## Target platform

Self-hosted SQL Server (`docs/adr/0008-audit-persistence-target.md`) — no managed backup/retention
service. Everything below is a manual job, not a cloud console setting.

## What's retained

`AuditRecords` only (append-only, DB-enforced immutability per
`20260718053127_AddAuditRecordImmutabilityTrigger` — see ticket 13 item 2). Not the operational
tables (`Transaction`, `WebhookInboxEntry`, etc.) — those are covered by ordinary DB backup policy,
not this 7-year requirement.

## Policy

1. **Full backup cadence**: nightly full backup of the database (not just `AuditRecords` — SQL
   Server backs up at the database granularity; a table-level backup isn't a supported primitive).
   Transaction log backups every 15 minutes for point-in-time recovery within the retention window.
2. **Retention window**: 7 years from `OccurredAtUtc` of the oldest row a backup set covers. Backup
   sets are labeled with their coverage date range; a set is eligible for deletion only once every
   `AuditRecord` it covers is older than 7 years AND a newer backup set already covers the current
   data (never delete the only backup covering a still-in-window record).
3. **Storage**: backups written to a location distinct from the primary SQL Server host (separate
   disk/volume at minimum; off-host/off-site preferred once that infrastructure exists) — a host
   failure must not take out both the live table and its backups.
4. **Immutability of the backup itself**: write-once storage (or equivalent — e.g. object storage
   with retention lock) once such storage exists, so an operator with backup-share access can't
   silently rewrite history the DB trigger already protects against at the table level. Not
   implemented yet — flagged so it isn't lost when a backup target is chosen.
5. **Purge**: no automated purge job runs at all initially. A 7-year-old backup set is deleted
   manually, by an operator, with the deletion itself logged (who, when, which set) — the 7-year
   number is a *minimum* retention, not a trigger for automatic deletion the day it's crossed.
6. **Verification**: quarterly restore-test of the most recent backup set to a scratch instance,
   confirming `AuditRecords` row count and a spot-checked row's `PayloadJson` match the source.
   Untested backups are not a retention policy.

## Explicitly not decided here

- Exact backup storage target (local volume vs NAS vs eventual off-site) — depends on infra not
  yet provisioned.
- Automation tooling (SQL Server Agent job vs external scheduler) — deferred until an instance
  exists to schedule against.

## Re-open trigger

Re-open this runbook (move from "doc-only" to "executed") once a real, non-ephemeral self-hosted
SQL Server instance is provisioned — set up the actual backup jobs against this policy at that
point, don't re-derive it from scratch.
