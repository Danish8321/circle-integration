# 0009 — Secrets in AWS Secrets Manager, despite self-hosted SQL Server (ADR 0008)

Status: accepted, 2026-07-18

## Context

Phase 3 (docs/README.md line ~102) requires "secrets in managed store." ADR 0008 fixed
self-hosted SQL Server (Docker/VM) as the DB deployment target — no managed cloud database.
Naively, "managed store" could mean a self-hosted secrets vault (HashiCorp Vault OSS) to match
that self-hosted posture, or a cloud-managed one (Azure Key Vault, AWS Secrets Manager).

This repo already has a real, unavoidable AWS dependency: SNS is Circle Mint's own webhook
transport (docs/features/03-webhook-processing.md §3.1 — "v1/SNS-based," confirmed against live
Circle docs, not a choice this repo made). Ticket 21 (this Phase 3 plan) builds a real SNS
signature verifier. That means AWS SDK/credentials are already load-bearing infrastructure here,
regardless of the DB's deployment target.

## Decision

**Secrets go into AWS Secrets Manager**, not a self-hosted vault, and not Azure Key Vault.

- Avoids introducing a *second* cloud provider (Azure) into a repo that only otherwise touches
  AWS (SNS) — one cloud footprint, not two.
- Avoids standing up and operating a self-hosted secrets vault (HashiCorp Vault OSS) as
  incremental ops burden on top of the self-hosted SQL Server ADR 0008 already commits to.
- The DB deployment target (self-hosted SQL Server) and the secrets store (AWS Secrets Manager)
  are independent decisions — nothing about ADR 0008 requires secrets to be self-hosted too, and
  this repo's own webhook transport already forces an AWS relationship.

Secret values covered: Circle API credentials, the Circle webhook endpoint's expected SNS topic
ARN(s), and (per the demo-script/sandbox ticket) sandbox-specific credentials, once real ones
exist. Local dev keeps using `appsettings.Development.json`/user-secrets as today — Secrets
Manager is a Production-only concern, consistent with CLAUDE.md invariant 9's mock-mode/
environment-check pattern (Production is where this actually gets wired in).

## Consequences

- New dependency: AWS SDK for .NET (`AWSSDK.SecretsManager` or the `Microsoft.Extensions.
  Configuration`-integrated `Amazon.Extensions.Configuration.SecretsManager` package) — pre-scoped
  by this ADR, not ad hoc.
- Future secret-storage decisions in this repo default to AWS Secrets Manager; revisit this ADR if
  the SNS/AWS dependency is ever removed (e.g. Circle ships a v2-style webhook transport this repo
  migrates to) or if a second cloud relationship becomes unavoidable for another reason.
- Self-hosted SQL Server (ADR 0008) is unaffected — this ADR does not revisit that decision.
