# PII-at-rest (TDE) plan

Status: doc-only, not yet enabled — no non-ephemeral SQL Server instance exists to enable TDE on
(see `docs/adr/0008-audit-persistence-target.md`). Testcontainers instances are ephemeral
(destroyed per test run) and not a meaningful TDE target — enabling it there would prove nothing
about a real deployment's key-management story.

Source requirement: PRD §12 — entity-registration details and bank data encrypted at rest.

## Target platform

Self-hosted SQL Server (`docs/adr/0008-audit-persistence-target.md`) — TDE here means manual
certificate/key management, not a managed service's automatic key rotation (contrast: Azure SQL's
service-managed TDE, explicitly not the chosen target, see ADR 0008).

## What's in scope

Tables carrying the PII the PRD calls out:

- `EntityRegistrations` — `BusinessName`, `BusinessUniqueIdentifier`, address fields
  (`Country`/`State`/`City`/`Postcode`/`StreetName`/`BuildingNumber`).
- `LinkedBankAccounts` — `BeneficiaryName`, `AccountNumber`, `RoutingNumber`, `BankName`, billing
  address fields.

TDE in SQL Server encrypts at the database file level (data + log files + backups), not per-table
— once enabled it covers everything above (and everything else in the database) uniformly. No
per-column encryption is needed to satisfy this requirement given that.

## Plan

1. **Database Master Key (DMK)** created in `master`, protected by a strong password stored in a
   secrets manager (not a config file, not source control) — this repo has no secrets-manager
   integration yet, so this step is blocked on that existing, not just on a SQL Server instance.
2. **Server certificate** created in `master`, protected by the DMK. **Immediately back up the
   certificate and its private key** to a location distinct from the database backups (losing this
   certificate with no backup makes the encrypted database permanently unrecoverable — this is the
   single highest-severity operational risk in this plan).
3. **Database Encryption Key (DEK)** created in the application database, protected by the server
   certificate.
4. **Enable TDE** (`ALTER DATABASE ... SET ENCRYPTION ON`) — one-time operation, runs as a
   background scan; no application code or migration changes required, this is server/database
   configuration outside EF Core's model.
5. **Certificate rotation**: re-key on a fixed schedule (annually, minimum) or immediately on
   suspected compromise — rotating creates a new certificate/DEK pair without requiring
   re-encryption of existing data (TDE re-encrypts the DEK, not the data, on rotation).
6. **Verification**: confirm `sys.dm_database_encryption_keys` reports `encryption_state = 3`
   (encrypted) for the application database; confirm a raw copy of the `.mdf`/backup file is
   unreadable without the certificate.

## Explicitly not decided here

- Column-level `Always Encrypted` for the fields above — not planned; TDE's at-rest coverage is
  judged sufficient for this requirement (PRD §12 asks for at-rest encryption, not
  encrypted-in-use/blind-to-DBA guarantees). Revisit only if a stricter requirement surfaces.
- Secrets-manager choice for the DMK password — blocked on that infra existing.

## Re-open trigger

Re-open this plan (move from "doc-only" to "enabled") once a real, non-ephemeral self-hosted SQL
Server instance is provisioned and a secrets manager exists for the DMK password.
